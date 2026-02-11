using System;
using Armament.Client.Networking;
using NUnit.Framework;

namespace Armament.Client.Tests;

public class CharacterSlotStorageTests
{
    private const int MaxSlots = 6;
    private string accountSubject = string.Empty;

    [SetUp]
    public void SetUp()
    {
        accountSubject = $"test:{Guid.NewGuid():N}";
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < MaxSlots; i++)
        {
            CharacterSlotStorage.DeleteSlot(accountSubject, i);
        }
    }

    [Test]
    public void NextEmptySlot_IsZero_WhenNoCharactersExist()
    {
        var next = CharacterSlotStorage.GetNextEmptySlot(accountSubject, MaxSlots);
        Assert.That(next, Is.EqualTo(0));
        Assert.That(CharacterSlotStorage.GetFilledSlots(accountSubject, MaxSlots), Is.Empty);
    }

    [Test]
    public void SaveSlot_CreatesFilledSlotAndNextEmptyAdvances()
    {
        CharacterSlotStorage.SaveSlot(accountSubject, 0, "Warrior", "bastion", "spec.bastion.bulwark");

        var filled = CharacterSlotStorage.GetFilledSlots(accountSubject, MaxSlots);
        Assert.That(filled.Count, Is.EqualTo(1));
        Assert.That(filled[0], Is.EqualTo(0));
        Assert.That(CharacterSlotStorage.GetNextEmptySlot(accountSubject, MaxSlots), Is.EqualTo(1));
    }

    [Test]
    public void DeleteSlotAndCompact_ShiftsCharactersForward()
    {
        CharacterSlotStorage.SaveSlot(accountSubject, 0, "A", "bastion", "spec.bastion.bulwark");
        CharacterSlotStorage.SaveSlot(accountSubject, 1, "B", "exorcist", "spec.exorcist.warden");
        CharacterSlotStorage.SaveSlot(accountSubject, 2, "C", "gunslinger", "spec.gunslinger.akimbo");

        CharacterSlotStorage.DeleteSlotAndCompact(accountSubject, 1, MaxSlots);

        Assert.That(CharacterSlotStorage.TryLoadSlot(accountSubject, 0, out var n0, out _, out _), Is.True);
        Assert.That(n0, Is.EqualTo("A"));

        Assert.That(CharacterSlotStorage.TryLoadSlot(accountSubject, 1, out var n1, out var c1, out var s1), Is.True);
        Assert.That(n1, Is.EqualTo("C"));
        Assert.That(c1, Is.EqualTo("gunslinger"));
        Assert.That(s1, Is.EqualTo("spec.gunslinger.akimbo"));

        Assert.That(CharacterSlotStorage.IsSlotEmpty(accountSubject, 2), Is.True);
        Assert.That(CharacterSlotStorage.GetNextEmptySlot(accountSubject, MaxSlots), Is.EqualTo(2));
    }
}
