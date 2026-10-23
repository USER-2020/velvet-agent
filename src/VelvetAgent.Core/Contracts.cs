using System.Text.Json.Serialization;
namespace VelvetAgent.Core;

public enum EnrollmentState { NotEnrolled, Enrolled, Revoked }
public record StationIdentity(Guid InstallationId, string Hostname);
public record AgentConfiguration(string MasterUrl = "https://localhost", int HeartbeatSeconds = 30, bool Development = false)
{
    public string? StudioId { get; init; }
    public void Validate()
    {
        if (!Uri.TryCreate(MasterUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Master requiere HTTPS sin credenciales en URL");
        if (HeartbeatSeconds is < 5 or > 3600) throw new ArgumentException("Intervalo permitido: 5–3600 segundos");
    }
}
public record SystemMetrics(string Hostname, string OperatingSystem, string Architecture, int CpuCount, double? CpuPercent, long? RamTotalBytes, long? RamAvailableBytes, double? RamUsedPercent, long? DiskAvailableBytes);
public record ObsStatus(bool ProcessRunning, bool WebSocketConnected, bool? Streaming = null, string? CurrentScene = null, string[]? Scenes = null, string? Error = null);
public record AgentStatus(StationIdentity Identity, EnrollmentState Enrollment, bool MasterConnected, DateTimeOffset? LastSync, string Version, double UptimeSeconds, SystemMetrics Metrics, ObsStatus Obs, string Mode)
{
    public bool UserAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? UserDisplayName { get; init; }
    public string? UserRole { get; init; }
    [JsonIgnore]
    public string[] UserPermissions { get; init; } = [];
    [JsonIgnore]
    public string[] UserRoles { get; init; } = [];
    public bool ConfigurationAvailable { get; init; }
    public Guid? SessionId { get; init; }
    public DateTimeOffset? SessionStartedAt { get; init; }
}
public record AgentHeartbeat(Guid Id, DateTimeOffset SentAt, AgentStatus Status);
public record AgentCommand(Guid Id, string Operation, DateTimeOffset ExpiresAt);
public record CommandExecutionResult(Guid CommandId, bool Success, string Message);
public record EnrollmentRequest(Guid InstallationId, string Hostname, string Token);
public record EnrollmentResponse(string DeviceToken);
public record WorkSessionStartRequest(Guid InstallationId, int StationId, string StudioId, int UserId);
public record WorkSessionResponse(string SessionId, DateTimeOffset StartedAt, string Status);
public record AgentEvent(Guid Id, DateTimeOffset At, string Kind, string Message);
public record OAuthTokenSet(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, DateTimeOffset RefreshExpiresAt)
{
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    public string[] Permissions { get; init; } = [];
    public string[] Roles { get; init; } = [];
    public bool ConfigurationAvailable { get; init; }
    public Guid? SessionId { get; init; }
    public DateTimeOffset? SessionStartedAt { get; init; }
    public int? StationId { get; init; }
    public string? MasterUrl { get; init; }
}
public record LocalRequest(int Version, string Operation, string? Token = null, ObsStatus? Obs = null, OAuthTokenSet? Auth = null);
public record LocalResponse(int Version, bool Success, object? Data, string? Error = null);
public interface IObsController { Task<ObsStatus> GetStatusAsync(CancellationToken cancellationToken); }
public interface IMetricsProvider { SystemMetrics Collect(); }
public interface ISecretStore { Task<string?> ReadAsync(CancellationToken ct); Task WriteAsync(string secret, CancellationToken ct); Task DeleteAsync(CancellationToken ct); }
public interface IMasterClient
{
    Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken ct);
    Task HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken ct);
    Task<WorkSessionResponse> StartSessionAsync(WorkSessionStartRequest request, CancellationToken ct);
    Task EndSessionAsync(string sessionId, string reason, CancellationToken ct);
    Task StartTransmissionAsync(string sessionId, CancellationToken ct);
    Task StopTransmissionAsync(string sessionId, CancellationToken ct);
    Task<AgentConfiguration?> ConfigurationAsync(CancellationToken ct);
    Task EventAsync(AgentEvent value, CancellationToken ct);
    Task ResultAsync(CommandExecutionResult result, CancellationToken ct);
    Task RevokeOAuthAsync(string accessToken, CancellationToken ct);
}
public static class CommandPolicy
{
    public static string? Validate(AgentCommand command, DateTimeOffset now, ISet<Guid> executed) => command.Id == Guid.Empty ? "Identificador inválido" : command.ExpiresAt <= now ? "Comando expirado" : command.Operation != "RefreshStatus" ? "Operación no permitida" : executed.Contains(command.Id) ? "Comando repetido" : null;
}
public static class RetryPolicy
{
    public static TimeSpan Delay(int attempt, double jitter) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Clamp(attempt, 0, 6))) + Math.Clamp(jitter, 0, 1));
}

// Future authenticated Reverb delivery; no subscription is enabled in the MVP.
public interface IRealtimeCommandSource { IAsyncEnumerable<AgentCommand> ReadAsync(CancellationToken cancellationToken); }
