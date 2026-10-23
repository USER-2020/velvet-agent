using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using VelvetAgent.Core;
namespace VelvetAgent.Infrastructure;

public sealed class MetricsProvider : IMetricsProvider
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, Extended; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("libSystem.dylib")] static extern int sysctlbyname(string name, out long value, ref nuint size, IntPtr input, nuint length);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("libSystem.dylib")] static extern uint mach_host_self();
    [DllImport("libSystem.dylib")] static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref uint count);
    readonly object gate = new();
    ulong? previousTotal, previousIdle;
    double? CpuUsage()
    {
        ulong total, idle;
        if (OperatingSystem.IsWindows()) { if (!GetSystemTimes(out idle, out var kernel, out var user)) return null; total = kernel + user; }
        else if (OperatingSystem.IsMacOS()) { var ticks = new uint[4]; uint count = 4; if (host_statistics(mach_host_self(), 3, ticks, ref count) != 0) return null; idle = ticks[2]; total = (ulong)ticks[0] + ticks[1] + ticks[2] + ticks[3]; }
        else return null;
        lock (gate) { double? usage = previousTotal.HasValue && total > previousTotal.Value && idle >= previousIdle!.Value ? Math.Clamp(100.0 * (1.0 - (double)(idle - previousIdle.Value) / (total - previousTotal.Value)), 0, 100) : null; previousTotal = total; previousIdle = idle; return usage; }
    }
    public SystemMetrics Collect()
    {
        long? total = null, available = null;
        if (OperatingSystem.IsWindows()) { var m = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; if (GlobalMemoryStatusEx(ref m)) { total = (long)m.TotalPhys; available = (long)m.AvailPhys; } }
        if (OperatingSystem.IsMacOS()) { nuint size = 8; if (sysctlbyname("hw.memsize", out var bytes, ref size, IntPtr.Zero, 0) == 0) total = bytes; }
        long? disk = null; try { disk = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!).AvailableFreeSpace; } catch (IOException) { }
        return new(Environment.MachineName, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), Environment.ProcessorCount, CpuUsage(), total, available, total is > 0 && available.HasValue ? 100.0 * (total.Value - available.Value) / total.Value : null, disk);
    }
}
public sealed class ProcessObsController : IObsController
{
    public async Task<ObsStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); bool running = false;
        foreach (var name in new[] { "obs64", "obs32", "obs", "OBS" }) foreach (var p in Process.GetProcessesByName(name)) { using (p) running = true; }
        if (!running) return new ObsStatus(false, false, Error: "OBS no está abierto");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri("ws://127.0.0.1:4455"), timeout.Token);
            var hello = await ReceiveAsync(socket, timeout.Token);
            if (hello.GetProperty("op").GetInt32() != 0) throw new InvalidDataException("Protocolo OBS inválido");
            if (hello.GetProperty("d").TryGetProperty("authentication", out _))
                return new ObsStatus(true, false, Error: "OBS requiere autenticación");
            await SendAsync(socket, new { op = 1, d = new { rpcVersion = 1 } }, timeout.Token);
            var identified = await ReceiveAsync(socket, timeout.Token);
            if (identified.GetProperty("op").GetInt32() != 2) throw new InvalidDataException("OBS no identificado");
            var stream = await RequestAsync(socket, "GetStreamStatus", "agent-stream", timeout.Token);
            var scenes = await RequestAsync(socket, "GetSceneList", "agent-scenes", timeout.Token);
            var streamData = stream.GetProperty("d").GetProperty("responseData");
            var sceneData = scenes.GetProperty("d").GetProperty("responseData");
            var names = sceneData.GetProperty("scenes").EnumerateArray()
                .Select(x => x.GetProperty("sceneName").GetString())
                .Where(x => x is not null).Cast<string>().ToArray();
            return new ObsStatus(true, true, streamData.GetProperty("outputActive").GetBoolean(), sceneData.GetProperty("currentProgramSceneName").GetString(), names);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new ObsStatus(true, false, Error: "WebSocket OBS sin respuesta"); }
        catch (Exception e) when (e is WebSocketException or IOException or InvalidDataException or JsonException) { return new ObsStatus(true, false, Error: "WebSocket OBS no disponible"); }
    }

    static async Task SendAsync(ClientWebSocket socket, object value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
    static async Task<JsonElement> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096]; WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(chunk, ct); if (result.MessageType == WebSocketMessageType.Close) throw new IOException(); buffer.Write(chunk, 0, result.Count); }
        while (!result.EndOfMessage);
        using var doc = JsonDocument.Parse(buffer.ToArray()); return doc.RootElement.Clone();
    }
    static async Task<JsonElement> RequestAsync(ClientWebSocket socket, string type, string id, CancellationToken ct)
    {
        await SendAsync(socket, new { op = 6, d = new { requestType = type, requestId = id } }, ct);
        while (true)
        {
            var value = await ReceiveAsync(socket, ct);
            if (value.GetProperty("op").GetInt32() != 7 || value.GetProperty("d").GetProperty("requestId").GetString() != id) continue;
            var status = value.GetProperty("d").GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean()) throw new InvalidDataException("OBS rechazó la consulta");
            return value;
        }
    }
}
