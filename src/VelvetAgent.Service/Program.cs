using VelvetAgent.Core;
using VelvetAgent.Infrastructure;
using VelvetAgent.Service;
var builder = Host.CreateApplicationBuilder(args);
var defaults = new AgentConfiguration(Environment.GetEnvironmentVariable("VELVET_MASTER_URL") ?? "https://localhost", int.TryParse(Environment.GetEnvironmentVariable("VELVET_HEARTBEAT_SECONDS"), out var interval) ? interval : 30, Environment.GetEnvironmentVariable("VELVET_DEVELOPMENT") == "1");
defaults = defaults with { StudioId = Environment.GetEnvironmentVariable("VELVET_STUDIO_ID") };
defaults.Validate();
var directory = Environment.GetEnvironmentVariable("VELVET_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetAgent");
var logDirectory = Environment.GetEnvironmentVariable("VELVET_LOG_DIR") ?? (OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VELVET Agent", "logs")
    : Path.Combine(Path.GetFullPath(directory), "logs"));
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(); builder.Logging.AddProvider(new RollingFileLoggerProvider(logDirectory));
var state = new LocalState(Path.GetFullPath(directory));
await state.IdentityAsync();
var persisted = await state.ConfigurationAsync(defaults);
var config = persisted with { MasterUrl = Environment.GetEnvironmentVariable("VELVET_MASTER_URL") ?? persisted.MasterUrl, HeartbeatSeconds = int.TryParse(Environment.GetEnvironmentVariable("VELVET_HEARTBEAT_SECONDS"), out var overrideInterval) ? overrideInterval : persisted.HeartbeatSeconds, Development = Environment.GetEnvironmentVariable("VELVET_DEVELOPMENT") == "1", StudioId = Environment.GetEnvironmentVariable("VELVET_STUDIO_ID") ?? persisted.StudioId };
config.Validate();
builder.Services.AddWindowsService(o => o.ServiceName = "VelvetAgent");
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(state);
builder.Services.AddSingleton(new DeviceSecretStore(Path.GetFullPath(directory)));
builder.Services.AddSingleton(new OAuthSecretStore(Path.GetFullPath(directory)));
builder.Services.AddSingleton<IMetricsProvider, MetricsProvider>(); builder.Services.AddSingleton<IObsController, ProcessObsController>();
if (config.Development) builder.Services.AddSingleton<IMasterClient, DevelopmentMasterClient>();
else builder.Services.AddSingleton<IMasterClient>(sp => new MasterClient(new HttpClient { BaseAddress = new Uri(config.MasterUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) }, sp.GetRequiredService<DeviceSecretStore>(), sp.GetRequiredService<OAuthSecretStore>()));
builder.Services.AddSingleton<AgentRuntime>(sp => new AgentRuntime(state, sp.GetRequiredService<IMetricsProvider>(), sp.GetRequiredService<IObsController>(), sp.GetRequiredService<IMasterClient>(), sp.GetRequiredService<DeviceSecretStore>(), sp.GetRequiredService<OAuthSecretStore>(), config));
builder.Services.AddHostedService<Worker>(); builder.Services.AddHostedService<LocalServer>();
await builder.Build().RunAsync();
