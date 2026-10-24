using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using VelvetAgent.Core;
namespace VelvetAgent.Service;

public sealed class LocalServer : BackgroundService
{
    readonly AgentRuntime runtime;
    readonly ILogger<LocalServer> logger;
    readonly string pipeName;

    public LocalServer(AgentRuntime runtime, ILogger<LocalServer> logger, string? pipeName = null)
    {
        this.runtime = runtime;
        this.logger = logger;
        this.pipeName = string.IsNullOrWhiteSpace(pipeName) ? "velvet-agent-v1" : pipeName;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = CreatePipe(pipeName);
            await pipe.WaitForConnectionAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                bool admin = !OperatingSystem.IsWindows();
                if (OperatingSystem.IsWindows()) admin = IsAdministrator(pipe);
                using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var chars = new char[16384]; int count = 0;
                while (count < chars.Length) { int n = await reader.ReadAsync(chars.AsMemory(count, 1), deadline.Token); if (n == 0 || chars[count] == '\n') break; count++; }
                if (count == chars.Length) throw new InvalidDataException();
                var request = JsonSerializer.Deserialize<LocalRequest>(new string(chars, 0, count), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException();
                LocalResponse response;
                try { response = await runtime.DispatchAsync(request, admin, deadline.Token); } catch (Exception e) when (e is not OperationCanceledException) { response = new(1, false, null, "Operación fallida; revise enrolamiento y conectividad"); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), deadline.Token);
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { logger.LogWarning("Petición local rechazada ({Type})", e.GetType().Name); }
        }
    }
    [SupportedOSPlatform("windows")]
    static bool IsAdministrator(NamedPipeServerStream pipe)
    {
        bool admin = false;
        try
        {
            pipe.RunAsClient(() => { using var user = WindowsIdentity.GetCurrent(true); admin = user is not null && new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator); });
        }
        catch (Exception)
        {
            // A client identity query must never terminate the pipe server. The
            // request remains read-only when the identity cannot be inspected.
            admin = false;
        }
        return admin;
    }
    static NamedPipeServerStream CreatePipe(string pipeName)
    {
        if (!OperatingSystem.IsWindows()) return new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var acl = new PipeSecurity();
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 16384, 16384, acl);
    }
}
