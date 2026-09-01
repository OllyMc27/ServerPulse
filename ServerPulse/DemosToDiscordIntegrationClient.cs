using System.Reflection;
using System.Text.Json;

namespace ServerPulse;

public sealed record DemosToDiscordCaseRequest(
    string SourceEventId,
    DateTime CapturedAtUtc,
    string ServerId,
    string ServerName,
    long? LegacyServerId,
    string Game,
    string Map,
    string Mode,
    int TargetClientId,
    long TargetNetworkId,
    string TargetName,
    string Category,
    string Accusation,
    IReadOnlyList<string> Context,
    int AdminClientId,
    string AdminName,
    string Notes);

public sealed class DemosToDiscordIntegrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> SubmitAsync(DemosToDiscordCaseRequest request, CancellationToken token)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name?.Equals("DemosToDiscord", StringComparison.OrdinalIgnoreCase) == true)
            ?.GetType("DemosToDiscord.ServerPulseIntegrationBridge", false, false);
        var method = type?.GetMethod("SubmitAsync", BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(CancellationToken)]);
        if (method is null)
            throw new InvalidOperationException("DemosToDiscord is not loaded or does not support ServerPulse case handoff.");

        var result = method.Invoke(null, [JsonSerializer.Serialize(request, JsonOptions), token]);
        if (result is not Task<string> task)
            throw new InvalidOperationException("DemosToDiscord returned an invalid case handoff response.");
        return await task;
    }
}
