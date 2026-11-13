namespace PawRescue.Infrastructure;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "PawRescue.Api";
    public string Audience { get; set; } = "PawRescue.Mobile";
    /// <summary>At least 32 characters. Keep it in user-secrets / env vars, never in git.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}

public class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>"Local" (dev) or "Cloudinary" (prod).</summary>
    public string Provider { get; set; } = "Local";
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
    public CloudinarySettings Cloudinary { get; set; } = new();
}

public class CloudinarySettings
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string RootFolder { get; set; } = "paw-rescue";
}

public class FirebaseOptions
{
    public const string Section = "Firebase";
    /// <summary>Path to the Firebase service-account JSON. Empty = push disabled (logged only).</summary>
    public string? CredentialsPath { get; set; }
}

public class SeedOptions
{
    public const string Section = "Seed";
    public string AdminEmail { get; set; } = "admin@pawrescue.local";
    public string AdminPassword { get; set; } = string.Empty;
    /// <summary>Fill an empty database with demo reports in the Philippines (see SampleDataSeeder).</summary>
    public bool SampleData { get; set; }
}
