using Armament.GameServer;
using Armament.ServerHost;

var port = 9000;
var simulationHz = 60;
var snapshotHz = 20;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedPort))
            {
                port = parsedPort;
                i++;
            }
            break;
        case "--simulation-hz":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSimHz))
            {
                simulationHz = parsedSimHz;
                i++;
            }
            break;
        case "--snapshot-hz":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSnapshotHz))
            {
                snapshotHz = parsedSnapshotHz;
                i++;
            }
            break;
    }
}

var dbConnection = Environment.GetEnvironmentVariable("ARMAMENT_DB_CONNECTION");
PersistenceBackedLootSink? persistenceBackedLootSink = null;
PersistenceBackedCharacterProfileService? characterProfileService = null;

if (!string.IsNullOrWhiteSpace(dbConnection))
{
    persistenceBackedLootSink = new PersistenceBackedLootSink(dbConnection);
    await persistenceBackedLootSink.InitializeAsync(CancellationToken.None);
    characterProfileService = new PersistenceBackedCharacterProfileService(dbConnection);
    Console.WriteLine("[Server] Persistence queue enabled.");
}
else
{
    Console.WriteLine("[Server] Persistence queue disabled (set ARMAMENT_DB_CONNECTION to enable).");
}

await using var server = new AuthoritativeServer(
    port,
    simulationHz,
    snapshotHz,
    persistenceBackedLootSink?.Sink,
    characterProfileService);
server.Start();

Console.WriteLine("[Server] Press Ctrl+C to stop.");

var stop = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Set();
};

stop.Wait();

if (persistenceBackedLootSink is not null)
{
    Console.WriteLine($"[Server] Persistence queue stats: processed={persistenceBackedLootSink.ProcessedCount}, dropped={persistenceBackedLootSink.DroppedCount}, failed={persistenceBackedLootSink.FailedCount}");
    await persistenceBackedLootSink.DisposeAsync();
}

if (characterProfileService is not null)
{
    await characterProfileService.DisposeAsync();
}
