using VelvetAgent.Core;
using VelvetAgent.Infrastructure;
namespace VelvetAgent.Service;

public sealed class AgentRuntime(LocalState state, IMetricsProvider metrics, IObsController obs, IMasterClient master, ISecretStore secrets, AgentConfiguration configuration)
{
    readonly SemaphoreSlim enrollmentLock = new(1, 1);
    readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    StationIdentity? identity;
    ObsStatus? latestObs;
    public bool Connected { get; private set; }
    public DateTimeOffset? LastSync { get; private set; }
    public EnrollmentState Enrollment { get; private set; }
    public async Task<AgentStatus> StatusAsync(CancellationToken ct)
    {
        identity ??= await state.IdentityAsync(ct);
        var obsStatus = await obs.GetStatusAsync(ct);
        if (!obsStatus.WebSocketConnected && latestObs?.WebSocketConnected == true) obsStatus = latestObs;
        var session = await OAuthSessionAsync(ct);
        var result = new AgentStatus(identity, Enrollment, Connected, LastSync, typeof(AgentRuntime).Assembly.GetName().Version?.ToString() ?? "unknown", (DateTimeOffset.UtcNow - started).TotalSeconds, metrics.Collect(), obsStatus, configuration.Development ? "development-adapter" : "master-api") { UserAuthenticated = session is not null, UserId = session?.UserId, UserDisplayName = session?.DisplayName, UserRole = session?.Roles.FirstOrDefault(), UserPermissions = session?.Permissions ?? [], UserRoles = session?.Roles ?? [], ConfigurationAvailable = session?.ConfigurationAvailable == true, SessionId = session?.SessionId, SessionStartedAt = session?.SessionStartedAt };
        return result;
    }
    public async Task InitializeAsync(CancellationToken ct)
    {
        identity = await state.IdentityAsync(ct);
        var raw = await secrets.ReadAsync(ct);
        try
        {
            var oauth = raw is null ? null : System.Text.Json.JsonSerializer.Deserialize<OAuthTokenSet>(raw);
            if (oauth is not null && (string.IsNullOrWhiteSpace(oauth.MasterUrl) || !Uri.TryCreate(oauth.MasterUrl, UriKind.Absolute, out var previous) || !Uri.TryCreate(configuration.MasterUrl, UriKind.Absolute, out var current) || !string.Equals(previous.AbsoluteUri.TrimEnd('/'), current.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            {
                await secrets.DeleteAsync(ct);
                raw = null;
            }
        }
        catch (System.Text.Json.JsonException) { }
        Enrollment = raw is null ? EnrollmentState.NotEnrolled : EnrollmentState.Enrolled;
    }
    public async Task TickAsync(CancellationToken ct)
    {
        if (!configuration.Development && Enrollment != EnrollmentState.Enrolled) return;
        try { await master.HeartbeatAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, await StatusAsync(ct)), ct); Connected = true; LastSync = DateTimeOffset.UtcNow; }
        catch (DeviceRevokedException) { Connected = false; Enrollment = EnrollmentState.Revoked; await secrets.DeleteAsync(ct); }
        catch { Connected = false; throw; }
    }
    public async Task EnrollAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsControl)) throw new ArgumentException("Token inválido");
        await enrollmentLock.WaitAsync(ct);
        try { identity ??= await state.IdentityAsync(ct); var r = await master.EnrollAsync(new(identity.InstallationId, identity.Hostname, token), ct); if (string.IsNullOrWhiteSpace(r.DeviceToken) || r.DeviceToken.Any(char.IsControl)) throw new InvalidDataException("Credencial inválida"); await secrets.WriteAsync(r.DeviceToken, ct); Enrollment = EnrollmentState.Enrolled; } finally { enrollmentLock.Release(); }
    }
    public async Task<LocalResponse> DispatchAsync(LocalRequest request, bool admin, CancellationToken ct)
    {
        if (request.Version != 1) return new(1, false, null, "Versión no admitida");
        object? data = request.Operation switch
        {
            "GetAgentStatus" => await StatusAsync(ct),
            "GetSystemMetrics" => metrics.Collect(),
            "GetObsStatus" => await obs.GetStatusAsync(ct),
            "UpdateObsStatus" when request.Obs is not null => UpdateObs(request.Obs),
            "StartTransmission" => await StartTransmissionAsync(ct),
            "StopTransmission" => await StopTransmissionAsync(ct),
            "SetOAuthSession" when request.Auth is not null => await SetOAuthSessionAsync(request.Auth, ct),
            "RevokeOAuthSession" => await RevokeOAuthSessionAsync(ct),
            "GetConfiguration" => configuration,
            "RequestEnrollment" when admin => await EnrollResultAsync(request.Token, ct),
            _ => null
        };
        return data is null ? new(1, false, null, "Operación no permitida") : new(1, true, data);
    }
    async Task<object> EnrollResultAsync(string? token, CancellationToken ct) { await EnrollAsync(token ?? "", ct); return new { Enrolled = true }; }
    object UpdateObs(ObsStatus value) { latestObs = value; return new { Updated = true }; }
    async Task<object> SetOAuthSessionAsync(OAuthTokenSet value, CancellationToken ct)
    {
        if (value.AccessToken.Length is < 20 or > 4096 || value.RefreshToken.Length is < 20 or > 4096 || value.AccessToken.Any(char.IsControl) || value.RefreshToken.Any(char.IsControl)) throw new ArgumentException("Sesión OAuth inválida");
        if (!int.TryParse(value.UserId, out var userId) || value.StationId is null || identity is null || string.IsNullOrWhiteSpace(configuration.StudioId)) throw new InvalidDataException("Identidad de estación incompleta");
        var previous = await OAuthSessionAsync(ct);
        if (previous?.SessionId is not null && previous.UserId == value.UserId && previous.StationId == value.StationId && string.Equals(previous.MasterUrl, value.MasterUrl, StringComparison.OrdinalIgnoreCase))
        {
            // Reautenticación o reinicio del Agent: reutilizar la sesión oficial
            // evita que Master responda 409 por una sesión ya activa.
            value = value with { SessionId = previous.SessionId, SessionStartedAt = previous.SessionStartedAt };
        }
        else
        {
            // El cliente HTTP obtiene el Bearer desde el almacén local; guardar
            // primero los tokens permite autenticar la llamada sessions/start.
            await secrets.WriteAsync(System.Text.Json.JsonSerializer.Serialize(value), ct);
            var startedSession = await master.StartSessionAsync(new(identity.InstallationId, value.StationId.Value, configuration.StudioId, userId), ct);
            if (!Guid.TryParse(startedSession.SessionId, out var masterSessionId)) throw new InvalidDataException("sessionId inválido de Master");
            value = value with { SessionId = masterSessionId, SessionStartedAt = startedSession.StartedAt };
        }
        await secrets.WriteAsync(System.Text.Json.JsonSerializer.Serialize(value), ct);
        await secrets.WriteAsync(System.Text.Json.JsonSerializer.Serialize(value), ct);
        Enrollment = EnrollmentState.Enrolled;
        return new { Authenticated = true, AccessExpiresAt = value.AccessExpiresAt };
    }
    async Task<object> StartTransmissionAsync(CancellationToken ct) { var session = await OAuthSessionAsync(ct) ?? throw new InvalidOperationException("Sesión no autenticada"); if (session.SessionId is null) throw new InvalidOperationException("Sesión de trabajo no iniciada"); await master.StartTransmissionAsync(session.SessionId.Value.ToString(), ct); return new { Started = true }; }
    async Task<object> StopTransmissionAsync(CancellationToken ct) { var session = await OAuthSessionAsync(ct) ?? throw new InvalidOperationException("Sesión no autenticada"); if (session.SessionId is null) throw new InvalidOperationException("Sesión de trabajo no iniciada"); await master.StopTransmissionAsync(session.SessionId.Value.ToString(), ct); return new { Stopped = true }; }
    async Task<object> RevokeOAuthSessionAsync(CancellationToken ct)
    {
        var raw = await secrets.ReadAsync(ct);
        try
        {
            var oauth = raw is null ? null : System.Text.Json.JsonSerializer.Deserialize<OAuthTokenSet>(raw);
            if (oauth?.SessionId is not null) await master.EndSessionAsync(oauth.SessionId.Value.ToString(), "logout", ct);
            if (oauth is not null) await master.RevokeOAuthAsync(oauth.AccessToken, ct);
        }
        catch (System.Text.Json.JsonException) { }
        await secrets.DeleteAsync(ct);
        Enrollment = EnrollmentState.NotEnrolled;
        return new { Revoked = true };
    }
    async Task<bool> HasOAuthSessionAsync(CancellationToken ct)
    {
        var raw = await secrets.ReadAsync(ct);
        try { return raw is not null && System.Text.Json.JsonSerializer.Deserialize<OAuthTokenSet>(raw) is not null; }
        catch (System.Text.Json.JsonException) { return false; }
    }
    async Task<OAuthTokenSet?> OAuthSessionAsync(CancellationToken ct)
    {
        var raw = await secrets.ReadAsync(ct);
        try { return raw is null ? null : System.Text.Json.JsonSerializer.Deserialize<OAuthTokenSet>(raw); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
