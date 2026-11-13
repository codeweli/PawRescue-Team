using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PawRescue.Mobile.Models;

namespace PawRescue.Mobile.Services;

public static class ApiConfig
{
    // Android emulator reaches the host PC through 10.0.2.2.
    // On a real phone use your PC's LAN IP (e.g. http://192.168.1.10:5080) — same Wi-Fi.
    public static string BaseUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5080/" : "http://localhost:5080/";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Error coming from the API's ProblemDetails, with a user-friendly message.</summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public ApiException(HttpStatusCode status, string message) : base(message) => StatusCode = status;
}

/// <summary>
/// ENCAPSULATION: tokens are only touched through this class and live in SecureStorage
/// (Android Keystore / iOS Keychain), never in plain Preferences.
/// </summary>
public class TokenStore
{
    private const string AccessKey = "access_token";
    private const string RefreshKey = "refresh_token";
    private const string UserKey = "current_user";

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public UserDto? CurrentUser { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken) && CurrentUser is not null;
    public bool IsVolunteer => CurrentUser?.Role is Roles.Volunteer or Roles.Administrator;
    public bool IsAdmin => CurrentUser?.Role == Roles.Administrator;

    public async Task SaveAsync(AuthResponse auth)
    {
        AccessToken = auth.AccessToken;
        RefreshToken = auth.RefreshToken;
        CurrentUser = auth.User;
        await SecureStorage.Default.SetAsync(AccessKey, auth.AccessToken);
        await SecureStorage.Default.SetAsync(RefreshKey, auth.RefreshToken);
        await SecureStorage.Default.SetAsync(UserKey, JsonSerializer.Serialize(auth.User, ApiConfig.Json));
    }

    public async Task UpdateUserAsync(UserDto user)
    {
        CurrentUser = user;
        await SecureStorage.Default.SetAsync(UserKey, JsonSerializer.Serialize(user, ApiConfig.Json));
    }

    public async Task<bool> LoadAsync()
    {
        try
        {
            AccessToken = await SecureStorage.Default.GetAsync(AccessKey);
            RefreshToken = await SecureStorage.Default.GetAsync(RefreshKey);
            var json = await SecureStorage.Default.GetAsync(UserKey);
            CurrentUser = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<UserDto>(json, ApiConfig.Json);
        }
        catch
        {
            Clear(); // SecureStorage can fail after an app reinstall; just start fresh.
        }
        return !string.IsNullOrEmpty(RefreshToken);
    }

    public void Clear()
    {
        AccessToken = RefreshToken = null;
        CurrentUser = null;
        SecureStorage.Default.RemoveAll();
    }
}

/// <summary>
/// Adds "Authorization: Bearer ..." to every request and, on a 401,
/// refreshes the token once and retries — the user never notices expiry.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly TokenStore _tokens;
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    public AuthHeaderHandler(TokenStore tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Attach(request);
        var response = await base.SendAsync(request, ct);

        var isAuthCall = request.RequestUri?.AbsolutePath.Contains("/api/auth/") == true;
        if (response.StatusCode != HttpStatusCode.Unauthorized || isAuthCall || string.IsNullOrEmpty(_tokens.RefreshToken))
            return response;

        if (!await TryRefreshAsync(ct)) return response;

        // Retry once with the new token (content must be re-created for multipart requests).
        var retry = await CloneAsync(request);
        Attach(retry);
        response.Dispose();
        return await base.SendAsync(retry, ct);
    }

    private void Attach(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_tokens.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        await RefreshLock.WaitAsync(ct);
        try
        {
            using var refreshReq = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ApiConfig.BaseUrl), "api/auth/refresh"))
            {
                Content = JsonContent.Create(new RefreshTokenRequest(_tokens.RefreshToken!), options: ApiConfig.Json)
            };
            using var res = await base.SendAsync(refreshReq, ct);
            if (!res.IsSuccessStatusCode) { _tokens.Clear(); return false; }

            var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(ApiConfig.Json, ct);
            if (auth is null) return false;
            await _tokens.SaveAsync(auth);
            return true;
        }
        finally { RefreshLock.Release(); }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }
}

/// <summary>Thin typed wrapper around HttpClient. All API services use this.</summary>
public class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) => _http = http;

    public async Task<T> GetAsync<T>(string url, CancellationToken ct = default)
        => await ReadAsync<T>(await _http.GetAsync(url, ct), ct);

    public async Task<T> PostAsync<T>(string url, object? body, CancellationToken ct = default)
        => await ReadAsync<T>(await _http.PostAsJsonAsync(url, body, ApiConfig.Json, ct), ct);

    public async Task PostAsync(string url, object? body, CancellationToken ct = default)
        => await EnsureAsync(await _http.PostAsJsonAsync(url, body, ApiConfig.Json, ct));

    public async Task<T> PutAsync<T>(string url, object body, CancellationToken ct = default)
        => await ReadAsync<T>(await _http.PutAsJsonAsync(url, body, ApiConfig.Json, ct), ct);

    public async Task PutAsync(string url, object body, CancellationToken ct = default)
        => await EnsureAsync(await _http.PutAsJsonAsync(url, body, ApiConfig.Json, ct));

    public async Task<T> PatchAsync<T>(string url, object? body, CancellationToken ct = default)
        => await ReadAsync<T>(await _http.PatchAsJsonAsync(url, body, ApiConfig.Json, ct), ct);

    public async Task PatchAsync(string url, object? body = null, CancellationToken ct = default)
        => await EnsureAsync(await _http.PatchAsJsonAsync(url, body, ApiConfig.Json, ct));

    public async Task DeleteAsync(string url, CancellationToken ct = default)
        => await EnsureAsync(await _http.DeleteAsync(url, ct));

    public async Task<T> PostMultipartAsync<T>(string url, MultipartFormDataContent content, CancellationToken ct = default)
        => await ReadAsync<T>(await _http.PostAsync(url, content, ct), ct);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        await EnsureAsync(res);
        return (await res.Content.ReadFromJsonAsync<T>(ApiConfig.Json, ct))!;
    }

    private static async Task EnsureAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;

        string message = res.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please log in again.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Please wait a minute and try again.",
            _ => "Something went wrong. Please try again."
        };

        try
        {
            var problem = await res.Content.ReadFromJsonAsync<JsonElement>(ApiConfig.Json);
            if (problem.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                message = d.GetString()!;
            else if (problem.TryGetProperty("errors", out var errors)) // [ApiController] validation errors
                message = string.Join("\n", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            else if (problem.TryGetProperty("message", out var m))
                message = m.GetString()!;
        }
        catch { /* body wasn't JSON — keep default message */ }

        throw new ApiException(res.StatusCode, message);
    }
}

/// <summary>Wraps Shell dialogs so ViewModels never reference pages (keeps them unit-testable).</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message, string ok = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel");
    Task<string?> ActionSheetAsync(string title, params string[] options);
    Task<string?> PromptAsync(string title, string message, string placeholder = "");
}

public class DialogService : IDialogService
{
    private static Page Page => Shell.Current?.CurrentPage ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string title, string message, string ok = "OK")
        => MainThread.InvokeOnMainThreadAsync(() => Page.DisplayAlertAsync(title, message, ok));

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel")
        => MainThread.InvokeOnMainThreadAsync(() => Page.DisplayAlertAsync(title, message, accept, cancel));

    public async Task<string?> ActionSheetAsync(string title, params string[] options)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => Page.DisplayActionSheetAsync(title, "Cancel", null, options));
        return result == "Cancel" ? null : result;
    }

    public Task<string?> PromptAsync(string title, string message, string placeholder = "")
        => MainThread.InvokeOnMainThreadAsync(() => Page.DisplayPromptAsync(title, message, "OK", "Cancel", placeholder));
}
