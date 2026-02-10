using System;

namespace Armament.SharedSim.Sim;

public sealed class CharacterContext
{
    public uint CharacterId { get; set; }
    public ushort Level { get; set; } = 1;
    public uint Experience { get; set; }
    public int Currency { get; set; }

    public CharacterAttributes Attributes { get; set; } = CharacterAttributes.Default;
    public DerivedStats DerivedStats { get; private set; } = DerivedStats.Default;

    public void RecalculateDerivedStats(CharacterStatTuning tuning)
    {
        DerivedStats = CharacterMath.ComputeDerived(Attributes, tuning);
    }
}

public struct CharacterAttributes
{
    public int Might;
    public int Will;
    public int Alacrity;
    public int Constitution;

    public static CharacterAttributes Default => new()
    {
        Might = 10,
        Will = 10,
        Alacrity = 10,
        Constitution = 10
    };

    public void Clamp(int minValue = 1, int maxValue = 999)
    {
        Might = Math.Clamp(Might, minValue, maxValue);
        Will = Math.Clamp(Will, minValue, maxValue);
        Alacrity = Math.Clamp(Alacrity, minValue, maxValue);
        Constitution = Math.Clamp(Constitution, minValue, maxValue);
    }
}

public struct DerivedStats
{
    public int MaxHealth;
    public int MaxBuilderResource;
    public int MaxSpenderResource;

    public int BaseMeleeDamage;
    public int KnockbackPermille;
    public int SkillPotencyPermille;
    public int ResourceRegenPermillePerSecond;

    public int MoveSpeedMilliPerSecond;
    public int AttackSpeedPermille;

    public static DerivedStats Default => new()
    {
        MaxHealth = 100,
        MaxBuilderResource = 100,
        MaxSpenderResource = 100,
        BaseMeleeDamage = 10,
        KnockbackPermille = 1000,
        SkillPotencyPermille = 1000,
        ResourceRegenPermillePerSecond = 1000,
        MoveSpeedMilliPerSecond = 4500,
        AttackSpeedPermille = 1000
    };
}

public struct CharacterStatTuning
{
    public int BaseMaxHealth;
    public int BaseBuilderResource;
    public int BaseSpenderResource;
    public int BaseMeleeDamage;
    public int BaseMoveSpeedMilliPerSecond;

    public int HealthPerConstitution;
    public int ResourcePerConstitution;
    public int DamagePerMight;

    public int KnockbackPermillePerMight;
    public int SkillPotencyPermillePerWill;
    public int ResourceRegenPermillePerWill;

    public int MoveSpeedPermillePerAlacrity;
    public int AttackSpeedPermillePerAlacrity;

    public static CharacterStatTuning Default => new()
    {
        BaseMaxHealth = 100,
        BaseBuilderResource = 50,
        BaseSpenderResource = 50,
        BaseMeleeDamage = 8,
        BaseMoveSpeedMilliPerSecond = 4200,
        HealthPerConstitution = 12,
        ResourcePerConstitution = 4,
        DamagePerMight = 2,
        KnockbackPermillePerMight = 8,
        SkillPotencyPermillePerWill = 6,
        ResourceRegenPermillePerWill = 7,
        MoveSpeedPermillePerAlacrity = 5,
        AttackSpeedPermillePerAlacrity = 10
    };
}

public static class CharacterMath
{
    public static DerivedStats ComputeDerived(CharacterAttributes attributes, CharacterStatTuning tuning)
    {
        attributes.Clamp();

        var maxHealth = tuning.BaseMaxHealth + attributes.Constitution * tuning.HealthPerConstitution;
        var sharedResource = tuning.BaseBuilderResource + attributes.Constitution * tuning.ResourcePerConstitution;

        var moveSpeedPermille = 1000 + attributes.Alacrity * tuning.MoveSpeedPermillePerAlacrity;
        var moveSpeed = tuning.BaseMoveSpeedMilliPerSecond * moveSpeedPermille / 1000;

        return new DerivedStats
        {
            MaxHealth = maxHealth,
            MaxBuilderResource = sharedResource,
            MaxSpenderResource = tuning.BaseSpenderResource + attributes.Constitution * tuning.ResourcePerConstitution,
            BaseMeleeDamage = tuning.BaseMeleeDamage + attributes.Might * tuning.DamagePerMight,
            KnockbackPermille = 1000 + attributes.Might * tuning.KnockbackPermillePerMight,
            SkillPotencyPermille = 1000 + attributes.Will * tuning.SkillPotencyPermillePerWill,
            ResourceRegenPermillePerSecond = 1000 + attributes.Will * tuning.ResourceRegenPermillePerWill,
            MoveSpeedMilliPerSecond = moveSpeed,
            AttackSpeedPermille = 1000 + attributes.Alacrity * tuning.AttackSpeedPermillePerAlacrity
        };
    }
}
