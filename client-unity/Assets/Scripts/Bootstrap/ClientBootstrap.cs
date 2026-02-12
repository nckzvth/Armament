using System;
using System.Linq;
using Armament.Client.Networking;
using UnityEngine;

namespace Armament.Client.Bootstrap
{

public static class ClientBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureClientExists()
    {
        if (Application.isBatchMode && Environment.GetCommandLineArgs().Any(x => x.Contains("runTests", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (UnityEngine.Object.FindFirstObjectByType<UdpGameClient>() != null)
        {
            return;
        }

        var gameObject = new GameObject("Phase0Client");
        var client = gameObject.AddComponent<UdpGameClient>();
        client.SetAutoConnect(false);
        gameObject.AddComponent<StartMenuUi>();
        gameObject.AddComponent<WorldDebugRenderer>();
        gameObject.AddComponent<DebugHud>();
    }
}
}
