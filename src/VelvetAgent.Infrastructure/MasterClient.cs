using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VelvetAgent.Core;
namespace VelvetAgent.Infrastructure;

public sealed class DeviceRevokedException : Exception { }
public sealed class OAuthSessionRevokedException : Exception { }
enum CredentialKind { Device, OAuth }
public sealed class MasterClient(HttpClient http, ISecretStore deviceSecrets, ISecretStore oauthSecrets) : IMasterClient
{
    public MasterClient(HttpClient http, ISecretStore secrets) : this(http, secrets, secrets) { }
    static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    async Task<string> BearerAsync(CredentialKind kind, CancellationToken ct)
    {
        var secrets = kind == CredentialKind.Device ? deviceSecrets : oauthSecrets;
        var raw = await secrets.ReadAsync(ct) ?? throw new InvalidOperationException("Estación sin enrolar");
        if (kind == CredentialKind.OAuth)
        {
            try
            {
                var oauth = System.Text.Json.JsonSerializer.Deserialize<OAuthTokenSet>(raw);
                if (oauth is not null && !string.IsNullOrWhiteSpace(oauth.AccessToken))
                {
                    if (oauth.AccessExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30)) return oauth.AccessToken;
                    return await RefreshOAuthAsync(oauth, ct);
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return raw;
    }
    async Task<string> RefreshOAuthAsync(OAuthTokenSet current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/oauth/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "velvet-agent", ["refresh_token"] = current.RefreshToken }) };
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new OAuthSessionRevokedException();
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<OAuthWireToken>(ct) ?? throw new InvalidDataException("Respuesta OAuth vacía");
        var next = new OAuthTokenSet(token.AccessToken, token.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), DateTimeOffset.UtcNow.AddSeconds(token.RefreshExpiresIn))
        {
            UserId = current.UserId,
            DisplayName = current.DisplayName,
            Permissions = current.Permissions,
            Roles = current.Roles,
            ConfigurationAvailable = current.ConfigurationAvailable,
            SessionId = current.SessionId,
            SessionStartedAt = current.SessionStartedAt,
            StationId = current.StationId,
            MasterUrl = current.MasterUrl,
        };
        await oauthSecrets.WriteAsync(System.Text.Json.JsonSerializer.Serialize(next), ct);
        return next.AccessToken;
    }
    sealed record OAuthWireToken([property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken, [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken, [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] double ExpiresIn, [property: System.Text.Json.Serialization.JsonPropertyName("refresh_expires_in")] double RefreshExpiresIn);
    async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string key, CredentialKind? auth, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Idempotency-Key", key);
            if (auth is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await BearerAsync(auth.Value, ct));
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (auth is not null && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (auth == CredentialKind.OAuth) throw new OAuthSessionRevokedException();
                    throw new DeviceRevokedException();
                }
                if (((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) && attempt < 3) { await Task.Delay(RetryPolicy.Delay(attempt, Random.Shared.NextDouble()), ct); continue; }
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(ct);
                    throw new HttpRequestException($"Master respondió {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 500)]}", null, response.StatusCode);
                }
                return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<T>(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 3) { await Task.Delay(RetryPolicy.Delay(attempt, Random.Shared.NextDouble()), ct); }
            catch (HttpRequestException e) when (e.StatusCode is null && attempt < 3) { await Task.Delay(RetryPolicy.Delay(attempt, Random.Shared.NextDouble()), ct); }
        }
    }
    public async Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest r, CancellationToken ct) => await SendAsync<EnrollmentResponse>(HttpMethod.Post, "api/agent/enroll", r, new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(r.InstallationId.ToString() + ":" + r.Token)).AsSpan(0, 16)).ToString(), null, ct) ?? throw new InvalidDataException("Credenciales ausentes");
    public async Task HeartbeatAsync(AgentHeartbeat h, CancellationToken ct)
    {
        // Master requires OAuth when the heartbeat carries a work-session id.
        // A plain station heartbeat remains authenticated with the permanent
        // device credential.
        var credential = h.Status.SessionId is null ? CredentialKind.Device : CredentialKind.OAuth;
        await SendAsync<object>(HttpMethod.Post, "api/agent/heartbeat", h, h.Id.ToString(), credential, ct);
    }
    public async Task<WorkSessionResponse> StartSessionAsync(WorkSessionStartRequest r, CancellationToken ct) => await SendAsync<WorkSessionResponse>(HttpMethod.Post, "api/agent/sessions/start", r, Guid.NewGuid().ToString(), CredentialKind.OAuth, ct) ?? throw new InvalidDataException("Respuesta de sesión vacía");
    public async Task EndSessionAsync(string sessionId, string reason, CancellationToken ct) => await SendAsync<object>(HttpMethod.Post, $"api/agent/sessions/{sessionId}/end", new { reason }, Guid.NewGuid().ToString(), CredentialKind.OAuth, ct);
    public async Task StartTransmissionAsync(string sessionId, CancellationToken ct) => await SendAsync<object>(HttpMethod.Post, $"api/agent/sessions/{sessionId}/transmission/start", null, Guid.NewGuid().ToString(), CredentialKind.OAuth, ct);
    public async Task StopTransmissionAsync(string sessionId, CancellationToken ct) => await SendAsync<object>(HttpMethod.Post, $"api/agent/sessions/{sessionId}/transmission/stop", null, Guid.NewGuid().ToString(), CredentialKind.OAuth, ct);
    public Task<AgentConfiguration?> ConfigurationAsync(CancellationToken ct) => SendAsync<AgentConfiguration>(HttpMethod.Get, "api/agent/configuration", null, Guid.NewGuid().ToString(), CredentialKind.Device, ct);
    public async Task EventAsync(AgentEvent e, CancellationToken ct) => await SendAsync<object>(HttpMethod.Post, "api/agent/events", e, e.Id.ToString(), CredentialKind.Device, ct);
    public async Task ResultAsync(CommandExecutionResult r, CancellationToken ct) => await SendAsync<object>(HttpMethod.Post, $"api/agent/commands/{r.CommandId}/result", r, r.CommandId.ToString(), CredentialKind.Device, ct);
    public async Task RevokeOAuthAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/oauth/revoke");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
    }
}
public sealed class DevelopmentMasterClient : IMasterClient
{
    public int Heartbeats { get; private set; }
    public Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest r, CancellationToken ct) => throw new InvalidOperationException("Modo desarrollo no emite credenciales");
    public Task HeartbeatAsync(AgentHeartbeat h, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Heartbeats++; return Task.CompletedTask; }
    public Task<WorkSessionResponse> StartSessionAsync(WorkSessionStartRequest r, CancellationToken ct) => Task.FromResult(new WorkSessionResponse(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "active"));
    public Task EndSessionAsync(string sessionId, string reason, CancellationToken ct) => Task.CompletedTask;
    public Task StartTransmissionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task StopTransmissionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task<AgentConfiguration?> ConfigurationAsync(CancellationToken ct) => Task.FromResult<AgentConfiguration?>(null);
    public Task EventAsync(AgentEvent e, CancellationToken ct) => Task.CompletedTask;
    public Task ResultAsync(CommandExecutionResult r, CancellationToken ct) => Task.CompletedTask;
    public Task RevokeOAuthAsync(string accessToken, CancellationToken ct) => Task.CompletedTask;
}
