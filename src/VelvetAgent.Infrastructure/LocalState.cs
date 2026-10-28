using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VelvetAgent.Core;
namespace VelvetAgent.Infrastructure;

public sealed class LocalState(string directory)
{
    public string DirectoryPath { get; } = directory;
    public async Task<StationIdentity> IdentityAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(DirectoryPath, "identity.json");
        if (File.Exists(path)) return JsonSerializer.Deserialize<StationIdentity>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("Identidad inválida");
        var identity = new StationIdentity(Guid.NewGuid(), Environment.MachineName);
        await AtomicWriteAsync(path, JsonSerializer.Serialize(identity), ct); return identity;
    }
    public async Task<AgentConfiguration> ConfigurationAsync(AgentConfiguration defaults, CancellationToken ct = default)
    {
        var path = Path.Combine(DirectoryPath, "configuration.json");
        if (File.Exists(path)) { var config = JsonSerializer.Deserialize<AgentConfiguration>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("Configuración inválida"); config.Validate(); return config; }
        defaults.Validate(); Directory.CreateDirectory(DirectoryPath); await AtomicWriteAsync(path, JsonSerializer.Serialize(defaults), ct); return defaults;
    }
    public static async Task AtomicWriteAsync(string path, string value, CancellationToken ct)
    {
        var temp = path + "." + Guid.NewGuid() + ".tmp";
        try { await File.WriteAllTextAsync(temp, value, ct); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed class PlatformSecretStore(string directory, string scope = "credential") : ISecretStore
{
    string PathName => Path.Combine(directory, $"{scope}.dpapi");
    // Preserve the original Keychain service for the station credential so
    // existing macOS enrollments remain visible after OAuth storage is split.
    string KeychainService => scope == "credential" ? "studio.velvet.agent" : $"studio.velvet.agent.{scope}";
    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return File.Exists(PathName) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(PathName, ct), null, DataProtectionScope.CurrentUser)) : null;
        if (OperatingSystem.IsMacOS()) { var r = await KeychainAsync(["find-generic-password", "-s", KeychainService, "-a", directory, "-w"], ct); return r.Code == 0 ? r.Output.TrimEnd('\r', '\n') : null; }
        throw new PlatformNotSupportedException("Almacén seguro requiere Windows o macOS");
    }
    public async Task WriteAsync(string secret, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(PathName, ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser), ct); return; }
        if (OperatingSystem.IsMacOS())
        { // security receives secret through stdin in interactive mode, never argv.
            var input = "add-generic-password -U -s \"" + KeychainService + "\" -a \"" + directory.Replace("\"", "") + "\" -w \"" + secret.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\n";
            var r = await KeychainAsync(["-i"], ct, input); if (r.Code != 0) throw new IOException("No se pudo escribir en Keychain"); return;
        }
        throw new PlatformNotSupportedException();
    }
    public async Task DeleteAsync(CancellationToken ct) { if (OperatingSystem.IsWindows()) { File.Delete(PathName); return; } if (OperatingSystem.IsMacOS()) await KeychainAsync(["delete-generic-password", "-s", KeychainService, "-a", directory], ct); }
    static async Task<(int Code, string Output)> KeychainAsync(string[] args, CancellationToken ct, string? input = null)
    {
        var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        if (input is not null) await p.StandardInput.WriteAsync(input); p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync(ct); var error = p.StandardError.ReadToEndAsync(ct); await p.WaitForExitAsync(ct); await error; return (p.ExitCode, await output);
    }
}

public sealed class DeviceSecretStore(string directory) : ISecretStore
{
    readonly PlatformSecretStore inner = new(directory, "credential");
    public Task<string?> ReadAsync(CancellationToken ct) => inner.ReadAsync(ct);
    public Task WriteAsync(string secret, CancellationToken ct) => inner.WriteAsync(secret, ct);
    public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
}

public sealed class OAuthSecretStore(string directory) : ISecretStore
{
    readonly PlatformSecretStore inner = new(directory, "oauth");
    public Task<string?> ReadAsync(CancellationToken ct) => inner.ReadAsync(ct);
    public Task WriteAsync(string secret, CancellationToken ct) => inner.WriteAsync(secret, ct);
    public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
}
