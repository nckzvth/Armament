#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Armament.Client.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Armament.Client.Tests
{

public class OverworldConnectionPlayModeTests
{
    private Process? serverProcess;
    private const int Port = 19120;

    [UnityTest]
    public System.Collections.IEnumerator ClientReceivesSnapshotFromLocalServer()
    {
        StartServer();

        var gameObject = new GameObject("TestClient");
        var client = gameObject.AddComponent<UdpGameClient>();
        client.Configure("127.0.0.1", Port, "PlayModeTester");

        var waitUntil = Time.realtimeSinceStartup + 8f;
        while (!client.HasReceivedSnapshot && Time.realtimeSinceStartup < waitUntil)
        {
            yield return null;
        }

        Assert.That(client.HasReceivedSnapshot, Is.True, "Client did not receive world snapshot in time.");
        Assert.That(client.LatestEntities.Count, Is.GreaterThanOrEqualTo(1), "Snapshot did not contain entities.");

        UnityEngine.Object.Destroy(gameObject);
    }

    [TearDown]
    public void TearDown()
    {
        if (serverProcess is { HasExited: false })
        {
            serverProcess.Kill();
            serverProcess.WaitForExit(3000);
        }

        serverProcess?.Dispose();
        serverProcess = null;
    }

    private void StartServer()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var projectPath = Path.Combine(repoRoot, "server-dotnet", "Src", "ServerHost", "Armament.ServerHost.csproj");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -- --port {Port} --simulation-hz 60 --snapshot-hz 10",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.Environment["DOTNET_CLI_HOME"] = Path.Combine(repoRoot, ".dotnet_home");

        serverProcess = Process.Start(psi);
        Assert.That(serverProcess, Is.Not.Null, "Failed to start local server process.");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var line = serverProcess!.StandardOutput.ReadLine();
            if (line is null)
            {
                continue;
            }

            if (line.Contains("UDP listening", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var stdErr = serverProcess!.StandardError.ReadToEnd();
        Assert.Fail($"Server did not start in time. stderr: {stdErr}");
    }
}
}
