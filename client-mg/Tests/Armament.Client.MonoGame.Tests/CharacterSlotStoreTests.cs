using Armament.SharedSim.Sim;
using NUnit.Framework;

namespace Armament.Client.MonoGame.Tests;

[TestFixture]
public sealed class CharacterSlotStoreTests
{
    [Test]
    public void SaveSlot_NormalizesBaseClassAndSpec()
    {
        var store = new CharacterSlotStore();

        store.SaveSlot("local:tester", 0, new CharacterSlotRecord
        {
            Name = "My Slot",
            BaseClassId = "DREADWEAVER",
            SpecId = "spec.dreadweaver.weaver"
        });

        var ok = store.TryLoadSlot("local:tester", 0, out var loaded);

        Assert.That(ok, Is.True);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.BaseClassId, Is.EqualTo("dreadweaver"));
        Assert.That(loaded.SpecId, Is.EqualTo("spec.dreadweaver.deceiver"));
    }

    [Test]
    public void DeleteSlotAndCompact_ShiftsSlotsUp()
    {
        var store = new CharacterSlotStore();

        store.SaveSlot("local:tester", 0, new CharacterSlotRecord { Name = "A", BaseClassId = "bastion", SpecId = "spec.bastion.bulwark" });
        store.SaveSlot("local:tester", 1, new CharacterSlotRecord { Name = "B", BaseClassId = "exorcist", SpecId = "spec.exorcist.warden" });
        store.SaveSlot("local:tester", 2, new CharacterSlotRecord { Name = "C", BaseClassId = "tidebinder", SpecId = "spec.tidebinder.tempest" });

        store.DeleteSlotAndCompact("local:tester", 1, 6);

        Assert.That(store.TryLoadSlot("local:tester", 0, out var slot0), Is.True);
        Assert.That(slot0!.Name, Is.EqualTo("A"));

        Assert.That(store.TryLoadSlot("local:tester", 1, out var slot1), Is.True);
        Assert.That(slot1!.Name, Is.EqualTo("C"));

        Assert.That(store.TryLoadSlot("local:tester", 2, out _), Is.False);
        Assert.That(store.GetNextEmptySlot("local:tester", 6), Is.EqualTo(2));

        var filled = store.GetFilledSlots("local:tester", 6);
        Assert.That(filled, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void NormalizeSpecForClass_RejectsCrossClassSpec()
    {
        var normalized = ClassSpecCatalog.NormalizeSpecForClass("gunslinger", "spec.exorcist.warden");
        Assert.That(normalized, Is.EqualTo("spec.gunslinger.akimbo"));
    }
}
