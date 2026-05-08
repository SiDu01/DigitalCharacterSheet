namespace DigitalCharacterSheet.Models;

public sealed class ItemDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }
    public string ItemKind { get; set; } = "";
    public string VariantGroupName { get; set; } = "";
    public string VariantBaseName { get; set; } = "";
    public string TypeCode { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Tier { get; set; } = "";
    public bool RequiresAttunement { get; set; }
    public string AttunementText { get; set; } = "";
    public double? Weight { get; set; }
    public int? ValueCopper { get; set; }
    public bool IsWeapon { get; set; }
    public bool IsArmor { get; set; }
    public bool IsWondrous { get; set; }
    public bool IsConsumable { get; set; }
    public string Description { get; set; } = "";
    public ItemWeaponStats? WeaponStats { get; set; }
    public ItemArmorStats? ArmorStats { get; set; }
    public List<string> Properties { get; set; } = [];
    public List<ItemBonus> Bonuses { get; set; } = [];
    public List<string> AttachedSpells { get; set; } = [];
    public List<string> AttunementRequirements { get; set; } = [];
    public string ListName => string.IsNullOrWhiteSpace(VariantGroupName) ? Name : VariantGroupName;
    public string VariantLabel => string.IsNullOrWhiteSpace(VariantBaseName) ? Source : VariantBaseName;

    public string Category
    {
        get
        {
            if (IsWeapon || WeaponStats is not null)
            {
                return "Weapons";
            }

            if (IsArmor || ArmorStats is not null)
            {
                return "Armor";
            }

            if (ItemKind == "ItemGroup")
            {
                return "Item Groups";
            }

            if (IsConsumable || TypeCode is "P" or "A" or "AF")
            {
                return "Consumables";
            }

            if (TypeCode is "AT" or "INS" or "GS" or "T")
            {
                return "Tools";
            }

            if (TypeCode is "SCF" or "SC")
            {
                return "Spellcasting Focus";
            }

            if (ItemKind == "MagicItem" || Rarity is not "" and not "none")
            {
                return "Magic Items";
            }

            return "Adventuring Gear";
        }
    }

    public string AttackAbilityRule
    {
        get
        {
            if (WeaponStats is null)
            {
                return "";
            }

            if (Properties.Any(property => property.Contains("finesse", StringComparison.OrdinalIgnoreCase)))
            {
                return "STR or DEX";
            }

            if (WeaponStats.WeaponCategory.Contains("ranged", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(WeaponStats.RangeDisplay))
            {
                return "DEX";
            }

            return "STR";
        }
    }
}

public sealed class ItemWeaponStats
{
    public string WeaponCategory { get; set; } = "";
    public string DamageOne { get; set; } = "";
    public string DamageTwo { get; set; } = "";
    public string DamageTypeCode { get; set; } = "";
    public int? RangeNormal { get; set; }
    public int? RangeLong { get; set; }
    public string AmmoType { get; set; } = "";
    public int? Reload { get; set; }
    public string Mastery { get; set; } = "";

    public string DamageDisplay => string.IsNullOrWhiteSpace(DamageOne)
        ? ""
        : $"{DamageOne} {DamageTypeCode}".Trim();

    public string VersatileDamageDisplay => string.IsNullOrWhiteSpace(DamageTwo)
        ? ""
        : $"{DamageTwo} {DamageTypeCode}".Trim();

    public string RangeDisplay
    {
        get
        {
            if (RangeNormal is null)
            {
                return "";
            }

            return RangeLong is null ? $"{RangeNormal} ft." : $"{RangeNormal}/{RangeLong} ft.";
        }
    }
}

public sealed class ItemArmorStats
{
    public int? ArmorClass { get; set; }
    public string StrengthRequirement { get; set; } = "";
    public bool HasStealthDisadvantage { get; set; }
    public string ArmorCategory { get; set; } = "";
}

public sealed class ItemBonus
{
    public string BonusType { get; set; } = "";
    public string Value { get; set; } = "";
}
