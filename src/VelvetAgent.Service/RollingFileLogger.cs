using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VelvetAgent.Service;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    readonly string directory;
    readonly object gate = new();
    const long MaxFileBytes = 5 * 1024 * 1024;
    const int RetainedFiles = 14;

    public RollingFileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);
    public void Dispose() { }

    void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        try
        {
            var entry = new
            {
                at = DateTimeOffset.UtcNow,
                level = level.ToString(),
                category,
                eventId = eventId.Id,
                message = Redact(message),
                exception = exception is null ? null : new { type = exception.GetType().Name, message = Redact(exception.Message) },
            };
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                var path = CurrentPath();
                if (File.Exists(path) && new FileInfo(path).Length + line.Length > MaxFileBytes)
                    path = Path.Combine(directory, $"velvet-agent-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.log");
                File.AppendAllText(path, line);
                Prune();
            }
        }
        catch
        {
            // Logging must never stop the Agent if the disk or ACL is unavailable.
        }
    }

    string CurrentPath() => Path.Combine(directory, $"velvet-agent-{DateTime.UtcNow:yyyyMMdd}.log");

    void Prune()
    {
        foreach (var file in new DirectoryInfo(directory).GetFiles("velvet-agent-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(RetainedFiles))
            file.Delete();
    }

    static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)(Bearer\s+)[^\s]+", "$1[REDACTED]");
        return Regex.Replace(value, @"(?i)(access_token|refresh_token|code_verifier|token)\s*[:=]\s*[^,\s]+", "$1=[REDACTED]");
    }

    sealed class RollingFileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }

    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
