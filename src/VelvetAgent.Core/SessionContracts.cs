namespace VelvetAgent.Core;

public enum AgentSessionState
{
    NotAuthenticated,
    Authenticating,
    Authenticated,
    PreparingStation,
    Ready,
    ShiftActive,
    Streaming,
    EndingSession,
    SessionClosed,
    Error
}

public sealed record UserSession(
    string UserId,
    string DisplayName,
    string[] Roles,
    string? ModelProfileId,
    string? AssignedStationId,
    DateTimeOffset ExpiresAt);

public sealed record SoftwareStatus(
    string Name,
    bool Installed,
    string? Version,
    string State,
    string? Error = null);

public sealed record StationPreparation(
    AgentSessionState State,
    bool MasterConnected,
    bool StationAuthorized,
    SoftwareStatus Obs,
    bool CameraDetected,
    bool MicrophoneDetected,
    bool NetworkAvailable,
    string? Error = null);

public static class SessionTransitions
{
    public static bool IsAllowed(AgentSessionState from, AgentSessionState to) =>
        (from, to) switch
        {
            (AgentSessionState.NotAuthenticated, AgentSessionState.Authenticating) => true,
            (AgentSessionState.Authenticating, AgentSessionState.Authenticated) => true,
            (AgentSessionState.Authenticating, AgentSessionState.Error) => true,
            (AgentSessionState.Authenticated, AgentSessionState.PreparingStation) => true,
            (AgentSessionState.PreparingStation, AgentSessionState.Ready) => true,
            (AgentSessionState.PreparingStation, AgentSessionState.Error) => true,
            (AgentSessionState.Ready, AgentSessionState.ShiftActive) => true,
            (AgentSessionState.ShiftActive, AgentSessionState.Streaming) => true,
            (AgentSessionState.Streaming, AgentSessionState.ShiftActive) => true,
            (AgentSessionState.ShiftActive, AgentSessionState.EndingSession) => true,
            (AgentSessionState.Ready, AgentSessionState.EndingSession) => true,
            (AgentSessionState.EndingSession, AgentSessionState.SessionClosed) => true,
            (_, AgentSessionState.Error) => true,
            _ => false
        };
}
