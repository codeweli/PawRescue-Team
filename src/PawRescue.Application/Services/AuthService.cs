using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using PawRescue.Application.Common;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;

namespace PawRescue.Application.Services;

/// <summary>
/// SINGLE RESPONSIBILITY: only authentication concerns live here.
/// DEPENDENCY INJECTION: every collaborator arrives through the constructor as an interface.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly IAuditLogger _audit;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepository users, IRoleRepository roles, IRefreshTokenRepository refreshTokens,
                       IUnitOfWork uow, IPasswordHasher hasher, ITokenService tokens, IEmailSender email,
                       IAuditLogger audit, ILogger<AuthService> logger)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _uow = uow;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (await _users.EmailExistsAsync(request.Email, ct))
            throw new ConflictException("An account with this email already exists.");

        // Volunteers sign up as volunteers directly for the capstone.
        // FUTURE: volunteer applications approved by an admin / partner organization.
        var roleName = request.RegisterAsVolunteer ? RoleNames.Volunteer : RoleNames.User;
        var role = await _roles.GetByNameAsync(roleName, ct)
                   ?? throw new InvalidOperationException($"Role '{roleName}' is not seeded.");

        var user = new User(request.FullName, request.Email, _hasher.Hash(request.Password), role.Id, request.PhoneNumber);
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync("USER_REGISTERED", nameof(User), user.Id.ToString(), new { user.Email, Role = roleName }, ct);
        return await IssueTokensAsync(user, role.Name, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, ct);

        // Same message for "no user" and "wrong password" so attackers can't enumerate emails.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        if (!user.IsActive)
            throw new ForbiddenException("This account has been deactivated. Please contact an administrator.");

        user.RecordLogin();
        if (!string.IsNullOrWhiteSpace(request.DeviceToken))
            user.RegisterDevice(request.DeviceToken);

        await _audit.LogAsync("USER_LOGIN", nameof(User), user.Id.ToString(), null, ct);
        return await IssueTokensAsync(user, user.Role.Name, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = _tokens.HashToken(refreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct)
                     ?? throw new UnauthorizedException("Invalid refresh token.");

        if (!stored.IsActive)
        {
            // Token reuse detected → revoke every session of this user (possible theft).
            if (stored.RevokedAt is not null)
            {
                _logger.LogWarning("Refresh token reuse detected for user {UserId}", stored.UserId);
                await _refreshTokens.RevokeAllForUserAsync(stored.UserId, ct);
                await _uow.SaveChangesAsync(ct);
            }
            throw new UnauthorizedException("Refresh token expired or revoked. Please log in again.");
        }

        var user = await _users.GetWithRoleAsync(stored.UserId, ct)
                   ?? throw new UnauthorizedException("User no longer exists.");
        if (!user.IsActive) throw new ForbiddenException("This account has been deactivated.");

        // Rotation: issue a new token and revoke the old one.
        var newRaw = _tokens.GenerateRefreshToken();
        var newHash = _tokens.HashToken(newRaw);
        stored.Revoke(newHash);
        await _refreshTokens.AddAsync(new RefreshToken(user.Id, newHash, DateTime.UtcNow.AddDays(_tokens.RefreshTokenDays)), ct);

        var (access, expires) = _tokens.CreateAccessToken(user, user.Role.Name);
        await _uow.SaveChangesAsync(ct);
        return new AuthResponse(access, expires, newRaw, user.ToDto());
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var stored = await _refreshTokens.GetByHashAsync(_tokens.HashToken(refreshToken), ct);
        if (stored is { IsActive: true })
        {
            stored.Revoke();
            await _uow.SaveChangesAsync(ct);
        }
    }

    // ============================================================
    // TODO(MEMBER 1): Forgot password — send a reset code
    // ------------------------------------------------------------
    // 1. Find the user with _users.GetByEmailAsync(email, ct).
    //    If no user is found, just return (never tell the caller the email doesn't exist).
    // 2. Generate a 6-digit code with RandomNumberGenerator.GetInt32(100000, 1000000).
    // 3. Save ONLY the hash: user.SetPasswordResetToken(_tokens.HashToken(code), DateTime.UtcNow.AddMinutes(15));
    // 4. await _uow.SaveChangesAsync(ct);
    // 5. Send the code: await _email.SendAsync(user.Email, "Paw Rescue password reset", $"Your code is {code}...", ct);
    // 6. Audit it: await _audit.LogAsync("PASSWORD_RESET_REQUESTED", nameof(User), user.Id.ToString(), null, ct);
    // Look at LoginAsync above for the same style.
    // ============================================================
    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(email.Trim(), ct);
        if (user is null) return;
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.SetPasswordResetToken(_tokens.HashToken(code), DateTime.UtcNow.AddMinutes(15));
        await _uow.SaveChangesAsync(ct);
        await _email.SendAsync(user.Email, "Paw Rescue password reset",
            $"Your password reset code is {code}. It expires in 15 minutes.", ct);
        await _audit.LogAsync("PASSWORD_RESET_REQUESTED", nameof(User), user.Id.ToString(), null, ct);
    }

    // ============================================================
    // TODO(MEMBER 1): Reset password — check the code and set the new password
    // ------------------------------------------------------------
    // 1. Load the user by email.
    // 2. If the user is null OR !user.IsPasswordResetTokenValid(_tokens.HashToken(request.Token))
    //    → throw new ValidationException("The reset code is invalid or has expired.");
    // 3. user.ChangePassword(_hasher.Hash(request.NewPassword));
    // 4. Log out every device: await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);
    // 5. Save, then audit "PASSWORD_RESET".
    // ============================================================
    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim(), ct);
        if (user is null || !user.IsPasswordResetTokenValid(_tokens.HashToken(request.Token.Trim())))
            throw new ValidationException("The reset code is invalid or has expired.");
        user.ChangePassword(_hasher.Hash(request.NewPassword));
        await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("PASSWORD_RESET", nameof(User), user.Id.ToString(), null, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string roleName, CancellationToken ct)
    {
        var (access, expires) = _tokens.CreateAccessToken(user, roleName);
        var refreshRaw = _tokens.GenerateRefreshToken();

        await _refreshTokens.AddAsync(
            new RefreshToken(user.Id, _tokens.HashToken(refreshRaw), DateTime.UtcNow.AddDays(_tokens.RefreshTokenDays)), ct);
        await _uow.SaveChangesAsync(ct);

        var dto = user.ToDto() with { Role = roleName };
        return new AuthResponse(access, expires, refreshRaw, dto);
    }
}
