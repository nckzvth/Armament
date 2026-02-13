using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Armament.SharedSim.Protocol;
using Armament.SharedSim.Sim;

namespace Armament.Client.MonoGame;

internal sealed class UdpProtocolClient : IDisposable
{
    private readonly ConcurrentQueue<IProtocolMessage> inbox = new();
    private UdpClient? udp;
    private IPEndPoint? endpoint;
    private CancellationTokenSource? cts;

    public bool IsConnected => udp is not null;

    public void Connect(string host, int port)
    {
        if (udp is not null)
        {
            return;
        }

        cts = new CancellationTokenSource();
        udp = new UdpClient(0);
        endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        _ = ReceiveLoopAsync(cts.Token);
    }

    public void Send(IProtocolMessage message)
    {
        if (udp is null || endpoint is null)
        {
            return;
        }

        try
        {
            var payload = ProtocolCodec.Encode(message);
            udp.Send(payload, payload.Length, endpoint);
        }
        catch
        {
        }
    }

    public bool TryDequeue(out IProtocolMessage message)
    {
        return inbox.TryDequeue(out message!);
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
        udp?.Dispose();
        udp = null;
        endpoint = null;
        while (inbox.TryDequeue(out _))
        {
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (udp is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                if (ProtocolCodec.TryDecode(result.Buffer, out var msg) && msg is not null)
                {
                    inbox.Enqueue(msg);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
            }
        }
    }
}

internal sealed class ClientConfig
{
    private const string DefaultPathName = "config.json";

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
    public string AccountSubject { get; set; } = "local:dev-account";
    public string AccountDisplayName { get; set; } = "DevAccount";
    public int SelectedSlot { get; set; }
    public string CharacterName { get; set; } = "Character 1";
    public string BaseClassId { get; set; } = ClassSpecCatalog.NormalizeBaseClass(null);
    public string SpecId { get; set; } = ClassSpecCatalog.NormalizeSpecForClass(ClassSpecCatalog.NormalizeBaseClass(null), null);
    public float WorldZoom { get; set; } = 108f;
    public bool LinearWorldFiltering { get; set; } = false;

    public static ClientConfig Load()
    {
        var path = ConfigPath();
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<ClientConfig>(json);
                if (cfg is not null)
                {
                    cfg.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(cfg.BaseClassId);
                    cfg.SpecId = ClassSpecCatalog.NormalizeSpecForClass(cfg.BaseClassId, cfg.SpecId);
                    cfg.SelectedSlot = Math.Clamp(cfg.SelectedSlot, 0, 5);
                    if (string.IsNullOrWhiteSpace(cfg.CharacterName))
                    {
                        cfg.CharacterName = $"Character {cfg.SelectedSlot + 1}";
                    }
                    cfg.WorldZoom = Math.Clamp(cfg.WorldZoom, 70f, 180f);

                    return cfg;
                }
            }
        }
        catch
        {
        }

        return new ClientConfig();
    }

    public void Save()
    {
        try
        {
            BaseClassId = ClassSpecCatalog.NormalizeBaseClass(BaseClassId);
            SpecId = ClassSpecCatalog.NormalizeSpecForClass(BaseClassId, SpecId);
            SelectedSlot = Math.Clamp(SelectedSlot, 0, 5);
            if (string.IsNullOrWhiteSpace(CharacterName))
            {
                CharacterName = $"Character {SelectedSlot + 1}";
            }
            WorldZoom = Math.Clamp(WorldZoom, 70f, 180f);

            var path = ConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    public static string ConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Armament", "client-mg", DefaultPathName);
    }
}

internal sealed class CharacterSlotStore
{
    private const int MaxSlots = 6;
    private readonly Dictionary<string, CharacterSlotRecord?[]> slotsByAccount = new(StringComparer.OrdinalIgnoreCase);

    public static CharacterSlotStore Load()
    {
        var store = new CharacterSlotStore();
        var path = PathOnDisk();
        try
        {
            if (!File.Exists(path))
            {
                return store;
            }

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<CharacterSlotStoreDto>(json);
            if (dto?.Accounts is null)
            {
                return store;
            }

            foreach (var acc in dto.Accounts)
            {
                var arr = new CharacterSlotRecord?[MaxSlots];
                if (acc.Slots is not null)
                {
                    for (var i = 0; i < MaxSlots && i < acc.Slots.Count; i++)
                    {
                        var slot = acc.Slots[i];
                        if (slot is null || string.IsNullOrWhiteSpace(slot.Name))
                        {
                            continue;
                        }

                        slot.BaseClassId = ClassSpecCatalog.NormalizeBaseClass(slot.BaseClassId);
                        slot.SpecId = ClassSpecCatalog.NormalizeSpecForClass(slot.BaseClassId, slot.SpecId);
                        arr[i] = slot;
                    }
                }

                store.slotsByAccount[NormalizeSubject(acc.AccountSubject)] = arr;
            }
        }
        catch
        {
        }

        return store;
    }

    public void Save()
    {
        try
        {
            var dto = new CharacterSlotStoreDto
            {
                Accounts = slotsByAccount.Select(pair => new AccountSlotsDto
                {
                    AccountSubject = pair.Key,
                    Slots = pair.Value.ToList()
                }).ToList()
            };

            var path = PathOnDisk();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    public bool TryLoadSlot(string accountSubject, int slot, out CharacterSlotRecord? record)
    {
        var arr = EnsureAccountArray(accountSubject);
        slot = Math.Clamp(slot, 0, MaxSlots - 1);
        record = arr[slot];
        return record is not null && !string.IsNullOrWhiteSpace(record.Name);
    }

    public void SaveSlot(string accountSubject, int slot, CharacterSlotRecord record)
    {
        var arr = EnsureAccountArray(accountSubject);
        slot = Math.Clamp(slot, 0, MaxSlots - 1);
        arr[slot] = new CharacterSlotRecord
        {
            Name = record.Name.Trim(),
            BaseClassId = ClassSpecCatalog.NormalizeBaseClass(record.BaseClassId),
            SpecId = ClassSpecCatalog.NormalizeSpecForClass(record.BaseClassId, record.SpecId)
        };
    }

    public void DeleteSlotAndCompact(string accountSubject, int deletedSlot, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        deletedSlot = Math.Clamp(deletedSlot, 0, cap - 1);
        for (var i = deletedSlot; i < cap - 1; i++)
        {
            arr[i] = arr[i + 1];
        }

        arr[cap - 1] = null;
    }

    public List<int> GetFilledSlots(string accountSubject, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        var list = new List<int>(cap);
        for (var i = 0; i < cap; i++)
        {
            if (arr[i] is not null && !string.IsNullOrWhiteSpace(arr[i]!.Name))
            {
                list.Add(i);
            }
        }

        return list;
    }

    public int GetNextEmptySlot(string accountSubject, int maxSlots)
    {
        var arr = EnsureAccountArray(accountSubject);
        var cap = Math.Clamp(maxSlots, 1, MaxSlots);
        for (var i = 0; i < cap; i++)
        {
            if (arr[i] is null || string.IsNullOrWhiteSpace(arr[i]!.Name))
            {
                return i;
            }
        }

        return -1;
    }

    private CharacterSlotRecord?[] EnsureAccountArray(string subject)
    {
        var key = NormalizeSubject(subject);
        if (!slotsByAccount.TryGetValue(key, out var arr))
        {
            arr = new CharacterSlotRecord?[MaxSlots];
            slotsByAccount[key] = arr;
        }

        return arr;
    }

    public static string NormalizeSubject(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "local_guest";
        }

        return input.Trim().ToLowerInvariant()
            .Replace(":", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(" ", "_");
    }

    private static string PathOnDisk()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Armament", "client-mg", "character-slots.json");
    }
}

internal sealed class CharacterSlotRecord
{
    public string Name { get; set; } = string.Empty;
    public string BaseClassId { get; set; } = ClassSpecCatalog.NormalizeBaseClass(null);
    public string SpecId { get; set; } = ClassSpecCatalog.NormalizeSpecForClass(ClassSpecCatalog.NormalizeBaseClass(null), null);
}

internal sealed class CharacterSlotStoreDto
{
    public List<AccountSlotsDto> Accounts { get; set; } = new();
}

internal sealed class AccountSlotsDto
{
    public string AccountSubject { get; set; } = string.Empty;
    public List<CharacterSlotRecord?> Slots { get; set; } = new();
}
