using System.Text.Json;
using VelvetAgent.Core;
namespace VelvetAgent.Infrastructure;
// No command delivery channel is enabled in MVP. This executor only refreshes status.
public sealed class CommandExecutor(string dataDirectory, IMasterClient master)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<CommandExecutionResult> ExecuteAsync(AgentCommand command, Func<CancellationToken, Task> refresh, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(dataDirectory); var path = Path.Combine(dataDirectory, "commands.json");
            var records = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<Guid, CommandExecutionResult>>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException() : new();
            if (records.TryGetValue(command.Id, out var previous)) { await master.ResultAsync(previous, ct); return previous; }
            var rejection = CommandPolicy.Validate(command, DateTimeOffset.UtcNow, records.Keys.ToHashSet());
            var result = new CommandExecutionResult(command.Id, false, rejection ?? "Ejecución reservada; resultado final pendiente");
            if (rejection is null)
            {
                // Reserve durably before the effect: a crash must never replay an operation.
                records[command.Id] = result; await LocalState.AtomicWriteAsync(path, JsonSerializer.Serialize(records), ct);
                try { await refresh(ct); result = new(command.Id, true, "Estado actualizado"); } catch (Exception e) when (e is not OperationCanceledException) { result = new(command.Id, false, "No se pudo actualizar el estado"); }
                records[command.Id] = result; await LocalState.AtomicWriteAsync(path, JsonSerializer.Serialize(records), ct);
            }
            await master.ResultAsync(result, ct); return result;
        }
        finally { gate.Release(); }
    }
}
