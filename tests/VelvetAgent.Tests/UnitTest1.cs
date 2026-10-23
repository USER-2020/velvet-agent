using System.Net;
using System.Text.Json;
using VelvetAgent.Core;
using VelvetAgent.Infrastructure;
using VelvetAgent.Service;
namespace VelvetAgent.Tests;

public sealed class WindowsFactAttribute : FactAttribute { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requiere DPAPI en Windows real"; } }
public class AgentTests
{
    sealed class Secret : ISecretStore
    {
        public string? Value;
        public Task<string?> ReadAsync(CancellationToken ct) => Task.FromResult(Value);
        public Task WriteAsync(string s, CancellationToken ct) { Value = s; return Task.CompletedTask; }
        public Task DeleteAsync(CancellationToken ct) { Value = null; return Task.CompletedTask; }
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(send(r));
    }
    static string Temp() => Path.Combine(Path.GetTempPath(), "velvet-test-" + Guid.NewGuid());
    [Fact] public async Task IdentityPersists() { var path = Temp(); try { var first = await new LocalState(path).IdentityAsync(); Assert.Equal(first, await new LocalState(path).IdentityAsync()); Assert.NotEqual(Guid.Empty, first.InstallationId); } finally { Directory.Delete(path, true); } }
    [Fact] public void HeartbeatRoundTrips() { var identity = new StationIdentity(Guid.NewGuid(), "studio"); Assert.Equal(identity, JsonSerializer.Deserialize<StationIdentity>(JsonSerializer.Serialize(identity))); var c = new AgentCommand(Guid.NewGuid(), "RefreshStatus", DateTimeOffset.UtcNow); Assert.Equal(c, JsonSerializer.Deserialize<AgentCommand>(JsonSerializer.Serialize(c))); }
    [Fact] public void PkceChallengeIsValidAndUnique() { var first = OAuthPkceChallenge.Create(); var second = OAuthPkceChallenge.Create(); Assert.NotEqual(first.State, second.State); Assert.DoesNotContain("=", first.CodeChallenge); Assert.DoesNotContain("+", first.CodeChallenge); Assert.DoesNotContain("/", first.CodeChallenge); Assert.NotEqual(first.CodeVerifier, first.CodeChallenge); }
    [Theory][InlineData(AgentSessionState.NotAuthenticated, AgentSessionState.Authenticating, true)][InlineData(AgentSessionState.Authenticated, AgentSessionState.PreparingStation, true)][InlineData(AgentSessionState.Ready, AgentSessionState.Streaming, false)][InlineData(AgentSessionState.ShiftActive, AgentSessionState.Streaming, true)] public void SessionTransitionsAreExplicit(AgentSessionState from, AgentSessionState to, bool allowed) => Assert.Equal(allowed, SessionTransitions.IsAllowed(from, to));
    [Theory][InlineData("http://master", 30)][InlineData("https://master", 0)][InlineData("https://user:secret@master", 30)] public void ConfigurationRejectsUnsafe(string url, int interval) => Assert.Throws<ArgumentException>(() => new AgentConfiguration(url, interval).Validate());
    [Fact] public void RetryIsBoundedAndExponential() { Assert.True(RetryPolicy.Delay(2, 0) > RetryPolicy.Delay(1, 0)); Assert.InRange(RetryPolicy.Delay(100, 1).TotalSeconds, 60, 61); }
    [Theory][InlineData("PowerShell")][InlineData("cmd")][InlineData("StartStreaming")][InlineData("ChangeScene")] public void DeniesCommands(string operation) => Assert.Equal("Operación no permitida", CommandPolicy.Validate(new(Guid.NewGuid(), operation, DateTimeOffset.UtcNow.AddMinutes(1)), DateTimeOffset.UtcNow, new HashSet<Guid>()));
    [Fact] public void RejectsExpiredAndReplay() { var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; Assert.Equal("Comando expirado", CommandPolicy.Validate(new(id, "RefreshStatus", now), now, new HashSet<Guid>())); Assert.Equal("Comando repetido", CommandPolicy.Validate(new(id, "RefreshStatus", now.AddMinutes(1)), now, new HashSet<Guid> { id })); Assert.Null(CommandPolicy.Validate(new(id, "RefreshStatus", now.AddMinutes(1)), now, new HashSet<Guid>())); }
    [Fact] public void MetricsAreRealOrUnknown() { var m = new MetricsProvider().Collect(); Assert.Equal(Environment.MachineName, m.Hostname); Assert.True(m.CpuCount > 0); Assert.Null(m.CpuPercent); if (m.RamTotalBytes.HasValue) Assert.True(m.RamTotalBytes > 0); }
    [Fact] public async Task HttpRejectsRevokedDevice() { var secret = new Secret { Value = "test" }; var client = new MasterClient(new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized))) { BaseAddress = new("https://master/") }, secret); await Assert.ThrowsAsync<DeviceRevokedException>(() => client.ConfigurationAsync(default)); }
    [Fact] public async Task HttpDoesNotRetryBadRequest() { int calls = 0; var client = new MasterClient(new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.BadRequest); })) { BaseAddress = new("https://master/") }, new Secret()); await Assert.ThrowsAsync<HttpRequestException>(() => client.EnrollAsync(new(Guid.NewGuid(), "studio", "temporary"), default)); Assert.Equal(1, calls); }
    [Fact] public async Task HttpRetryPreservesIdempotency() { int calls = 0; string? key = null; var client = new MasterClient(new HttpClient(new Handler(r => { calls++; var current = r.Headers.GetValues("Idempotency-Key").Single(); if (key != null) Assert.Equal(key, current); key = current; return new(calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent); })) { BaseAddress = new("https://master/") }, new Secret { Value = "test" }); await client.EventAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "test", "test"), default); Assert.Equal(2, calls); }
    [Fact] public async Task HeartbeatAndLocalAuthorization() { var path = Temp(); try { var dev = new DevelopmentMasterClient(); var runtime = new AgentRuntime(new(path), new MetricsProvider(), new ProcessObsController(), dev, new Secret(), new(Development: true)); await runtime.InitializeAsync(default); await runtime.TickAsync(default); Assert.Equal(1, dev.Heartbeats); var status = await runtime.StatusAsync(default); Assert.True(status.MasterConnected); Assert.Equal("development-adapter", status.Mode); Assert.False((await runtime.DispatchAsync(new(2, "GetAgentStatus"), false, default)).Success); Assert.False((await runtime.DispatchAsync(new(1, "RequestEnrollment", "secret"), false, default)).Success); Assert.False((await runtime.DispatchAsync(new(1, "ExecuteShell"), true, default)).Success); Assert.True((await runtime.DispatchAsync(new(1, "GetAgentStatus"), false, default)).Success); } finally { Directory.Delete(path, true); } }
    [WindowsFact] public async Task WindowsDpapiRoundTrip() { var path = Temp(); try { var s = new PlatformSecretStore(path); await s.WriteAsync("test-credential", default); Assert.Equal("test-credential", await s.ReadAsync(default)); Assert.DoesNotContain("test-credential", await File.ReadAllTextAsync(Path.Combine(path, "credential.dpapi"))); await s.DeleteAsync(default); Assert.Null(await s.ReadAsync(default)); } finally { Directory.Delete(path, true); } }
    [Fact] public async Task ConfigurationPersistsAndValidates() { var path = Temp(); try { var state = new LocalState(path); var original = new AgentConfiguration("https://master.example", 15); Assert.Equal(original, await state.ConfigurationAsync(original)); Assert.Equal(original, await state.ConfigurationAsync(new())); await File.WriteAllTextAsync(Path.Combine(path, "configuration.json"), "{\"MasterUrl\":\"http://unsafe\",\"HeartbeatSeconds\":30}"); await Assert.ThrowsAsync<ArgumentException>(() => state.ConfigurationAsync(new())); } finally { Directory.Delete(path, true); } }
    [Fact] public async Task CommandsPersistReplayProtection() { var path = Temp(); try { var master = new DevelopmentMasterClient(); int effects = 0; var command = new AgentCommand(Guid.NewGuid(), "RefreshStatus", DateTimeOffset.UtcNow.AddMinutes(1)); Task Effect(CancellationToken _) { effects++; return Task.CompletedTask; } var first = await new CommandExecutor(path, master).ExecuteAsync(command, Effect, default); var repeated = await new CommandExecutor(path, master).ExecuteAsync(command, Effect, default); Assert.True(first.Success); Assert.Equal(first, repeated); Assert.Equal(1, effects); var denied = await new CommandExecutor(path, master).ExecuteAsync(new(Guid.NewGuid(), "shell", DateTimeOffset.UtcNow.AddMinutes(1)), Effect, default); Assert.False(denied.Success); Assert.Equal(1, effects); } finally { Directory.Delete(path, true); } }
    [Fact] public async Task NamedPipeExchangesVersionedStatus()
    {
        var path = Temp();
        var pipeName = "v";
        try
        {
            var runtime = new AgentRuntime(new(path), new MetricsProvider(), new ProcessObsController(), new DevelopmentMasterClient(), new Secret(), new(Development: true));
            await runtime.InitializeAsync(default);
            using var server = new LocalServer(runtime, Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalServer>.Instance, pipeName);
            await server.StartAsync(default);
            using (var client = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(3000);
                using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new LocalRequest(1, "GetAgentStatus")));
                var response = JsonDocument.Parse((await reader.ReadLineAsync())!);
                Assert.True(response.RootElement.GetProperty("Success").GetBoolean());
                Assert.Equal("development-adapter", response.RootElement.GetProperty("Data").GetProperty("Mode").GetString());
            }
            await server.StopAsync(default);
        }
        finally { Directory.Delete(path, true); }
    }
}
