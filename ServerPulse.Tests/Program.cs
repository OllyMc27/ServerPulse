using Microsoft.Extensions.Logging.Abstractions;

namespace ServerPulse.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("unique partial player name resolves", UniquePartialAsync),
            ("ambiguous partial remains unresolved", AmbiguousPartialAsync),
            ("ordinary accusation wording does not become a player match", StopWordAsync),
            ("guidance context and manual status are persisted", StoreWorkflowAsync)
        };
        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed");
        return failed == 0 ? 0 : 1;
    }

    private static Task UniquePartialAsync()
    {
        var resolved = GuidanceTargetResolver.ResolveUniqueClientId(
            "slc cheater", "cheater", "cheating", [(621, "SLC-Oberst"), (700, "Other Player")], 3, true);
        Equal(621, resolved, "SLC should uniquely resolve SLC-Oberst");
        return Task.CompletedTask;
    }

    private static Task AmbiguousPartialAsync()
    {
        var resolved = GuidanceTargetResolver.ResolveUniqueClientId(
            "slc cheater", "cheater", "cheating", [(621, "SLC-Oberst"), (622, "SLC-Zero")], 3, true);
        Equal<int?>(null, resolved, "ambiguous SLC fragments must remain unresolved");
        return Task.CompletedTask;
    }

    private static Task StopWordAsync()
    {
        var resolved = GuidanceTargetResolver.ResolveUniqueClientId(
            "nice cheating", "cheating", "cheating", [(621, "NiceGuy")], 3, true);
        Equal<int?>(null, resolved, "ordinary wording must not be treated as a target fragment");
        return Task.CompletedTask;
    }

    private static async Task StoreWorkflowAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ServerPulse.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = new ServerPulseConfig { StateFilePath = Path.Combine(directory, "state.json") };
            using var store = new AnalyticsStore(config, NullLogger<AnalyticsStore>.Instance);
            await store.LoadAsync(CancellationToken.None);
            var at = DateTimeOffset.UtcNow;
            var item = new PlayerGuidanceEventRecord
            {
                Id = "signal-1",
                EventType = "Accusation",
                ServerId = "server",
                CapturedAt = at,
                PlayersAtCapture = [new GuidancePlayerSnapshotRecord { ClientId = 621, NetworkId = 123, PlayerName = "SLC-Oberst" }]
            };
            store.AddPlayerGuidanceEvent(item);
            store.AppendPlayerGuidanceContext("server", new GuidanceContextMessageRecord
            {
                MessageId = "after",
                CapturedAt = at.AddSeconds(5),
                PlayerName = "Witness",
                Message = "I saw that too"
            }, TimeSpan.FromSeconds(30), 20);
            True(store.UpdatePlayerGuidanceEvent("signal-1", value =>
            {
                value.TargetClientId = 621;
                value.ReviewStatus = "ManuallyResolved";
            }), "event update should succeed");
            var saved = store.GetPlayerGuidanceEvent("signal-1")!;
            Equal(1, saved.ContextMessages.Count, "future context should append within the review window");
            Equal("ManuallyResolved", saved.ReviewStatus, "manual resolution status should persist");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
