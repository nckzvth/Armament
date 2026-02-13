using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;
using Armament.GameServer.Persistence;
using Armament.Persistence;
using Armament.Persistence.Abstractions;
using Armament.SharedSim.Sim;

namespace Armament.ServerHost;

public sealed class PersistenceBackedCharacterProfileService : ICharacterProfileService, IAsyncDisposable
{
    private readonly Channel<ProfileWorkItem> _requests;
    private readonly ConcurrentQueue<CharacterProfileLoadResult> _results = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly string _connectionString;

    public PersistenceBackedCharacterProfileService(string connectionString, int capacity = 256)
    {
        _connectionString = connectionString;
        _requests = Channel.CreateBounded<ProfileWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _worker = Task.Run(WorkerAsync);
    }

    public bool TryEnqueueLoad(CharacterProfileLoadRequest request)
    {
        return _requests.Writer.TryWrite(ProfileWorkItem.ForLoad(request));
    }

    public bool TryEnqueueSave(CharacterProfileSaveRequest request)
    {
        return _requests.Writer.TryWrite(ProfileWorkItem.ForSave(request));
    }

    public bool TryDequeueLoaded(out CharacterProfileLoadResult result)
    {
        return _results.TryDequeue(out result!);
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(_cts.Token))
            {
                if (request.IsLoad)
                {
                    Guid resolvedCharacterId = Guid.Empty;
                    string resolvedCharacterName = request.LoadRequest!.PreferredCharacterName;
                    CharacterProfileData? profile = null;
                    try
                    {
                        var loadResult = await LoadOrCreateAsync(request.LoadRequest!, _cts.Token);
                        resolvedCharacterId = loadResult.CharacterId;
                        resolvedCharacterName = loadResult.CharacterName;
                        profile = loadResult.Profile;
                    }
                    catch
                    {
                        profile = null;
                    }

                    _results.Enqueue(new CharacterProfileLoadResult(
                        request.LoadRequest!.EndpointKey,
                        resolvedCharacterId,
                        resolvedCharacterName,
                        profile));
                }
                else
                {
                    try
                    {
                        await SaveProfileAsync(request.SaveRequest!, _cts.Token);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<ResolvedCharacterLoad> LoadOrCreateAsync(CharacterProfileLoadRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = PersistenceModule.CreateDbContext(_connectionString);
        var (characters, _, _, _) = PersistenceModule.CreateRepositories(dbContext);
        var accounts = PersistenceModule.CreateAccountRepository(dbContext);

        var account = await accounts.GetOrCreateBySubjectAsync(
            string.IsNullOrWhiteSpace(request.AccountSubject) ? "local:guest" : request.AccountSubject,
            string.IsNullOrWhiteSpace(request.AccountDisplayName) ? "Guest" : request.AccountDisplayName,
            cancellationToken);

        var slot = Math.Clamp(request.CharacterSlot, 0, 7);
        var linked = await accounts.ListCharactersAsync(account.AccountId, cancellationToken);
        var slotRecord = linked.FirstOrDefault(x => x.SlotIndex == slot);

        var preferredName = string.IsNullOrWhiteSpace(request.PreferredCharacterName)
            ? $"Character {slot + 1}"
            : request.PreferredCharacterName.Trim();
        var baseClassId = ClassSpecCatalog.NormalizeBaseClass(request.RequestedBaseClassId);
        var specId = ClassSpecCatalog.NormalizeSpecForClass(baseClassId, request.RequestedSpecId);

        var characterId = slotRecord?.CharacterId ?? ComputeStableCharacterId(account.ExternalSubject, slot);
        var characterName = slotRecord?.CharacterName ?? preferredName;

        var existing = await characters.GetAsync(characterId, cancellationToken);
        if (existing is not null)
        {
            if (slotRecord is null)
            {
                await accounts.BindCharacterAsync(account.AccountId, characterId, slot, characterName, cancellationToken);
            }

            return new ResolvedCharacterLoad(
                characterId,
                characterName,
                new CharacterProfileData(
                    Level: existing.Level,
                    Experience: (uint)Math.Clamp(existing.Experience, 0, uint.MaxValue),
                    Currency: existing.Currency,
                    Attributes: new CharacterAttributes
                    {
                        Might = existing.Attributes.Might,
                        Will = existing.Attributes.Will,
                        Alacrity = existing.Attributes.Alacrity,
                        Constitution = existing.Attributes.Constitution
                    },
                    BaseClassId: existing.BaseClassId,
                    SpecId: existing.SpecId,
                    InventoryJson: string.IsNullOrWhiteSpace(existing.InventoryJson) ? "{}" : existing.InventoryJson,
                    QuestProgressJson: string.IsNullOrWhiteSpace(existing.QuestProgressJson) ? "{}" : existing.QuestProgressJson));
        }

        var derived = CharacterMath.ComputeDerived(CharacterAttributes.Default, CharacterStatTuning.Default);
        var created = new CharacterContextRecord(
            CharacterId: characterId,
            Name: characterName,
            BaseClassId: baseClassId,
            SpecId: specId,
            Level: 1,
            Experience: 0,
            Attributes: new CharacterAttributesRecord(
                CharacterAttributes.Default.Might,
                CharacterAttributes.Default.Will,
                CharacterAttributes.Default.Alacrity,
                CharacterAttributes.Default.Constitution),
            MaxHealth: derived.MaxHealth,
            MaxBuilderResource: derived.MaxBuilderResource,
            MaxSpenderResource: derived.MaxSpenderResource,
            Currency: 0,
            GearJson: "{}",
            InventoryJson: "{}",
            LearnedRecipesJson: "[]",
            QuestProgressJson: "{}");

        try
        {
            await characters.CreateAsync(created, cancellationToken);
        }
        catch
        {
            // Another worker/process may have inserted concurrently; fall through to reload.
        }

        await accounts.BindCharacterAsync(account.AccountId, characterId, slot, characterName, cancellationToken);
        var loaded = await characters.GetAsync(characterId, cancellationToken);
        if (loaded is null)
        {
            return new ResolvedCharacterLoad(characterId, characterName, new CharacterProfileData(1, 0, 0, CharacterAttributes.Default, baseClassId, specId, "{}"));
        }

        return new ResolvedCharacterLoad(
            characterId,
            characterName,
            new CharacterProfileData(
                Level: loaded.Level,
                Experience: (uint)Math.Clamp(loaded.Experience, 0, uint.MaxValue),
                Currency: loaded.Currency,
                Attributes: new CharacterAttributes
                {
                    Might = loaded.Attributes.Might,
                    Will = loaded.Attributes.Will,
                    Alacrity = loaded.Attributes.Alacrity,
                    Constitution = loaded.Attributes.Constitution
                },
                BaseClassId: loaded.BaseClassId,
                SpecId: loaded.SpecId,
                InventoryJson: string.IsNullOrWhiteSpace(loaded.InventoryJson) ? "{}" : loaded.InventoryJson,
                QuestProgressJson: string.IsNullOrWhiteSpace(loaded.QuestProgressJson) ? "{}" : loaded.QuestProgressJson));
    }

    private async Task SaveProfileAsync(CharacterProfileSaveRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = PersistenceModule.CreateDbContext(_connectionString);
        var (characters, _, _, _) = PersistenceModule.CreateRepositories(dbContext);

        await characters.UpsertProfileAsync(
            request.CharacterId,
            request.CharacterName,
            request.Profile.BaseClassId,
            request.Profile.SpecId,
            request.Profile.Level,
            request.Profile.Experience,
            request.Profile.Currency,
            new CharacterAttributesRecord(
                request.Profile.Attributes.Might,
                request.Profile.Attributes.Will,
                request.Profile.Attributes.Alacrity,
                request.Profile.Attributes.Constitution),
            request.Profile.InventoryJson,
            request.Profile.QuestProgressJson,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private sealed class ProfileWorkItem
    {
        public bool IsLoad { get; init; }
        public CharacterProfileLoadRequest? LoadRequest { get; init; }
        public CharacterProfileSaveRequest? SaveRequest { get; init; }

        public static ProfileWorkItem ForLoad(CharacterProfileLoadRequest request)
            => new() { IsLoad = true, LoadRequest = request };

        public static ProfileWorkItem ForSave(CharacterProfileSaveRequest request)
            => new() { IsLoad = false, SaveRequest = request };
    }

    private static Guid ComputeStableCharacterId(string accountSubject, int slot)
    {
        var normalizedSubject = (accountSubject ?? "local:guest").Trim().ToLowerInvariant();
        var data = System.Text.Encoding.UTF8.GetBytes($"armament-character:{normalizedSubject}:{slot}");
        var hash = System.Security.Cryptography.SHA256.HashData(data);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private sealed record ResolvedCharacterLoad(Guid CharacterId, string CharacterName, CharacterProfileData Profile);
}
