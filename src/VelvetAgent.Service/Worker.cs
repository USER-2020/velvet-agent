using VelvetAgent.Core;
namespace VelvetAgent.Service;

public sealed class Worker(AgentRuntime runtime, AgentConfiguration configuration, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await runtime.InitializeAsync(ct);
        logger.LogInformation("Agent iniciado. Adaptador de desarrollo: {Development}", configuration.Development);
        while (!ct.IsCancellationRequested)
        {
            try { await runtime.TickAsync(ct); logger.LogInformation("Heartbeat: conectado={Connected}", runtime.Connected); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Fallo de heartbeat ({Type}): {Message}; se reintentará", e.GetType().Name, e.Message); }
            await Task.Delay(TimeSpan.FromSeconds(configuration.HeartbeatSeconds), ct);
        }
    }
}
