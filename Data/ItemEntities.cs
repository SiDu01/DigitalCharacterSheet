using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("ItemDefinitions")]
public sealed class ItemDefinitionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }

    [Indexed]
    public string ItemKind { get; set; } = "";

    [Indexed]
    public string VariantGroupName { get; set; } = "";

    public string VariantBaseName { get; set; } = "";

    [Indexed]
    public string TypeCode { get; set; } = "";

    public string TypeSource { get; set; } = "";

    [Indexed]
    public string Rarity { get; set; } = "";

    [Indexed]
    public string Tier { get; set; } = "";

    [Indexed]
    public bool RequiresAttunement { get; set; }

    public string AttunementText { get; set; } = "";
    public double? Weight { get; set; }
    public int? ValueCopper { get; set; }

    [Indexed]
    public bool IsWeapon { get; set; }

    [Indexed]
    public bool IsArmor { get; set; }

    [Indexed]
    public bool IsWondrous { get; set; }

    [Indexed]
    public bool IsConsumable { get; set; }

    public bool IsStaff { get; set; }
    public bool IsWand { get; set; }
    public bool IsPotion { get; set; }
    public bool IsRod { get; set; }
    public bool IsAmmunition { get; set; }
    public bool HasFluff { get; set; }
    public bool HasFluffImages { get; set; }
    public bool IsSrd { get; set; }
    public bool IsBasicRules { get; set; }
    public bool IsSrd2024 { get; set; }
    public bool IsBasicRules2024 { get; set; }
    public string Description { get; set; } = "";
    public string RawJson { get; set; } = "";
}

[Table("ItemWeaponStats")]
public sealed class ItemWeaponStatEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    [Indexed]
    public string WeaponCategory { get; set; } = "";

    public string DamageOne { get; set; } = "";
    public string DamageTwo { get; set; } = "";
    public string DamageTypeCode { get; set; } = "";
    public int? RangeNormal { get; set; }
    public int? RangeLong { get; set; }
    public string AmmoType { get; set; } = "";
    public int? Reload { get; set; }
    public string Mastery { get; set; } = "";
}

[Table("ItemArmorStats")]
public sealed class ItemArmorStatEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    public int? ArmorClass { get; set; }
    public string StrengthRequirement { get; set; } = "";
    public bool HasStealthDisadvantage { get; set; }
    public string ArmorCategory { get; set; } = "";
}

[Table("ItemProperties")]
public sealed class ItemPropertyEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Abbreviation { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string RawJson { get; set; } = "";
}

[Table("ItemDefinitionProperties")]
public sealed class ItemDefinitionPropertyEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    [Indexed]
    public int ItemPropertyId { get; set; }
}

[Table("ItemTypes")]
public sealed class ItemTypeEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Abbreviation { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string RawJson { get; set; } = "";
}

[Table("ItemBonuses")]
public sealed class ItemBonusEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    [Indexed]
    public string BonusType { get; set; } = "";

    public string Value { get; set; } = "";
}

[Table("ItemAttachedSpells")]
public sealed class ItemAttachedSpellEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    [Indexed]
    public string SpellName { get; set; } = "";

    public string SpellSource { get; set; } = "";
    public int? ChargeCost { get; set; }
    public int? CastLevel { get; set; }
    public string RawJson { get; set; } = "";
}

[Table("ItemAttunementRequirements")]
public sealed class ItemAttunementRequirementEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemDefinitionId { get; set; }

    [Indexed]
    public string RequirementType { get; set; } = "";

    [Indexed]
    public string RequirementValue { get; set; } = "";

    public string RawJson { get; set; } = "";
}

[Table("ItemGroups")]
public sealed class ItemGroupEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }
    public string TypeCode { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Description { get; set; } = "";
    public string RawJson { get; set; } = "";
}

[Table("ItemGroupMembers")]
public sealed class ItemGroupMemberEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ItemGroupId { get; set; }

    [Indexed]
    public string ItemName { get; set; } = "";

    [Indexed]
    public string ItemSource { get; set; } = "";

    public int? ItemDefinitionId { get; set; }
}

[Table("MagicItemVariants")]
public sealed class MagicItemVariantEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }
    public string TypeCode { get; set; } = "";
    public string NamePrefix { get; set; } = "";
    public string NameSuffix { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Tier { get; set; } = "";
    public bool RequiresAttunement { get; set; }
    public string BonusWeapon { get; set; } = "";
    public string BonusAc { get; set; } = "";
    public string Description { get; set; } = "";
    public string RequiresJson { get; set; } = "";
    public string ExcludesJson { get; set; } = "";
    public string InheritsJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}

[Table("ItemFluff")]
public sealed class ItemFluffEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string ItemName { get; set; } = "";

    [Indexed]
    public string ItemSource { get; set; } = "";

    public int? ItemDefinitionId { get; set; }
    public string Description { get; set; } = "";
    public string ImagesJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}
