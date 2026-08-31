using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Events.Game;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Events.Server;
using SharedLibraryCore.Interfaces;
using SharedLibraryCore.Interfaces.Events;

namespace ServerPulse;

public sealed class Plugin : IPluginV2
{
    private readonly AnalyticsEngine _engine;
    private readonly ServerPulseWebfront _webfront;
    private readonly PlayerGuidanceService _playerGuidance;
    private readonly ServerPulseConfig _config;
    private readonly IConfigurationHandlerV2<ServerPulseConfig> _configurationHandler;
    private readonly ILogger<Plugin> _logger;
    private readonly bool _configurationChanged;
    private bool _disposed;

    public string Name => "ServerPulse";
    public string Author => "OllyMc27";
    public string Version => Utilities.GetVersionAsString();

    public static void RegisterDependencies(IServiceCollection services)
    {
        services.AddConfiguration("ServerPulse", new ServerPulseConfig());
        services.AddSingleton<AnalyticsStore>();
        services.AddSingleton<ChatSignalClassifier>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PlayerGuidanceDetectionEngine>();
        services.AddSingleton<PlayerGuidanceService>();
        services.AddSingleton<RecommendationEngine>();
        services.AddSingleton<AnalyticsEngine>();
        services.AddSingleton<ServerPulseWebfront>();
    }

    public Plugin(
        AnalyticsEngine engine,
        ServerPulseWebfront webfront,
        PlayerGuidanceService playerGuidance,
        ServerPulseConfig config,
        IConfigurationHandlerV2<ServerPulseConfig> configurationHandler,
        ILogger<Plugin> logger)
    {
        _engine = engine;
        _webfront = webfront;
        _playerGuidance = playerGuidance;
        _config = config;
        _configurationHandler = configurationHandler;
        _logger = logger;
        _configurationChanged = NormalizeConfiguration(config);

        IManagementEventSubscriptions.Load += OnLoad;
        IManagementEventSubscriptions.ClientStateAuthorized += OnClientAuthorized;
        IManagementEventSubscriptions.ClientStateDisposed += OnClientDisposed;
        IManagementEventSubscriptions.ClientPenaltyAdministered += OnPenalty;
        IGameEventSubscriptions.MatchStarted += OnMatchStarted;
        IGameEventSubscriptions.MatchEnded += OnMatchEnded;
        IGameEventSubscriptions.ClientEnteredMatch += OnClientEnteredMatch;
        IGameEventSubscriptions.ClientExitedMatch += OnClientExitedMatch;
        IGameEventSubscriptions.ClientMessaged += OnClientMessaged;
        IGameServerEventSubscriptions.MonitoringStarted += OnMonitoringStarted;
        IGameServerEventSubscriptions.MonitoringStopped += OnMonitoringStopped;
        IGameServerEventSubscriptions.ConnectionInterrupted += OnConnectionInterrupted;
        IGameServerEventSubscriptions.ConnectionRestored += OnConnectionRestored;
        _webfront.Register();

        _logger.LogInformation("[{Name}] {Version} by {Author} initialized", Name, Version, Author);
    }

    private async Task OnLoad(IManager manager, CancellationToken token)
    {
        if (_configurationChanged)
            await _configurationHandler.Set(_config);
        await _engine.StartAsync(manager, token);
        Console.WriteLine($"[{Name}] by {Author} loaded. Version: {Version}");
        Console.WriteLine($"[{Name}] analytics enabled: {_config.Enabled}; webfront: {_config.EnableWebfrontDashboard}; player guidance: {_config.PlayerGuidance.Enabled}; timezone: {AnalyticsTime.ConfigurationLabel}");
    }

    private Task OnClientAuthorized(ClientStateAuthorizeEvent value, CancellationToken token) => _engine.ClientAuthorizedAsync(value, token);
    private Task OnClientDisposed(ClientStateDisposeEvent value, CancellationToken token)
    {
        _playerGuidance.RemoveClientCooldowns(value.Client.ClientId);
        return _engine.ClientDisposedAsync(value, token);
    }
    private Task OnPenalty(ClientPenaltyEvent value, CancellationToken token) => _engine.PenaltyAsync(value, token);
    private Task OnMatchStarted(MatchStartEvent value, CancellationToken token) => _engine.MatchStartedAsync(value, token);
    private Task OnMatchEnded(MatchEndEvent value, CancellationToken token) => _engine.MatchEndedAsync(value, token);
    private Task OnClientEnteredMatch(ClientEnterMatchEvent value, CancellationToken token) => _engine.ClientEnteredMatchAsync(value, token);
    private Task OnClientExitedMatch(ClientExitMatchEvent value, CancellationToken token) => _engine.ClientExitedMatchAsync(value, token);
    private Task OnClientMessaged(ClientMessageEvent value, CancellationToken token) => _engine.ChatAsync(value, token);
    private Task OnMonitoringStarted(MonitorStartEvent value, CancellationToken token) => _engine.MonitoringStartedAsync(value, token);
    private Task OnMonitoringStopped(MonitorStopEvent value, CancellationToken token) => _engine.MonitoringStoppedAsync(value, token);
    private Task OnConnectionInterrupted(ConnectionInterruptEvent value, CancellationToken token) => _engine.ConnectionInterruptedAsync(value, token);
    private Task OnConnectionRestored(ConnectionRestoreEvent value, CancellationToken token) => _engine.ConnectionRestoredAsync(value, token);

    private bool NormalizeConfiguration(ServerPulseConfig config)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(config.TimeZone))
        {
            config.TimeZone = AnalyticsTime.DefaultTimeZoneId;
            changed = true;
        }
        if (!AnalyticsTime.Configure(config.TimeZone))
            _logger.LogWarning("[{Name}] time zone {TimeZone} was not recognised; using {Fallback}", Name, config.TimeZone, AnalyticsTime.DefaultTimeZoneId);
        if (string.IsNullOrWhiteSpace(config.StateFilePath))
        {
            config.StateFilePath = "Configuration/ServerPulseData.json";
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(config.AnonymizationSalt))
        {
            config.AnonymizationSalt = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            changed = true;
        }
        config.ExcludedServers ??= [];
        config.ChatCategories ??= ServerPulseConfig.DefaultChatCategories();
        config.PlayerGuidance ??= new PlayerGuidanceConfig();
        config.PlayerGuidance.Categories ??= PlayerGuidanceConfig.DefaultCategories();
        config.PlayerGuidance.ReminderMessages ??= PlayerGuidanceConfig.DefaultReminderMessages();
        config.PlayerGuidance.ExcludedPhrases ??= [];
        config.PlayerGuidance.CommunityReportPhrases ??= PlayerGuidanceConfig.DefaultCommunityReportPhrases();
        config.PlayerGuidance.CommunityReportExclusions ??= [];
        config.PlayerGuidance.ServerOverrides ??= new Dictionary<string, PlayerGuidanceServerOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in config.PlayerGuidance.Categories)
        {
            category.Phrases ??= [];
            category.RegexPatterns ??= [];
            category.ReminderMessages ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (var serverOverride in config.PlayerGuidance.ServerOverrides.Values)
            serverOverride.ExcludedPhrases ??= [];
        if (config.PlayerGuidance.ServerOverrides.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            config.PlayerGuidance.ServerOverrides = new Dictionary<string, PlayerGuidanceServerOverride>(config.PlayerGuidance.ServerOverrides, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        config.ServerOverrides ??= new Dictionary<string, ServerPulseServerOverride>(StringComparer.OrdinalIgnoreCase);
        if (config.ServerOverrides.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            config.ServerOverrides = new Dictionary<string, ServerPulseServerOverride>(config.ServerOverrides, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        return changed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        IManagementEventSubscriptions.Load -= OnLoad;
        IManagementEventSubscriptions.ClientStateAuthorized -= OnClientAuthorized;
        IManagementEventSubscriptions.ClientStateDisposed -= OnClientDisposed;
        IManagementEventSubscriptions.ClientPenaltyAdministered -= OnPenalty;
        IGameEventSubscriptions.MatchStarted -= OnMatchStarted;
        IGameEventSubscriptions.MatchEnded -= OnMatchEnded;
        IGameEventSubscriptions.ClientEnteredMatch -= OnClientEnteredMatch;
        IGameEventSubscriptions.ClientExitedMatch -= OnClientExitedMatch;
        IGameEventSubscriptions.ClientMessaged -= OnClientMessaged;
        IGameServerEventSubscriptions.MonitoringStarted -= OnMonitoringStarted;
        IGameServerEventSubscriptions.MonitoringStopped -= OnMonitoringStopped;
        IGameServerEventSubscriptions.ConnectionInterrupted -= OnConnectionInterrupted;
        IGameServerEventSubscriptions.ConnectionRestored -= OnConnectionRestored;
        _webfront.Dispose();
        _playerGuidance.Dispose();
        _engine.Dispose();
        _logger.LogInformation("[{Name}] unloaded", Name);
    }
}
