namespace DigitalCharacterSheet.Models;

public sealed class Spell
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }

    public int Level { get; set; }
    public string SchoolCode { get; set; } = "";
    public string School { get; set; } = "";

    public int? CastingTimeNumber { get; set; }
    public string CastingTimeUnit { get; set; } = "";
    public string CastingTimeCondition { get; set; } = "";
    public string CastingTimeNote { get; set; } = "";

    public string RangeType { get; set; } = "";
    public string RangeDistanceType { get; set; } = "";
    public int? RangeDistanceAmount { get; set; }

    public bool HasVerbalComponent { get; set; }
    public bool HasSomaticComponent { get; set; }
    public bool HasMaterialComponent { get; set; }
    public string MaterialComponent { get; set; } = "";
    public int? MaterialCostCopper { get; set; }
    public bool ConsumesMaterial { get; set; }
    public bool HasRoyaltyComponent { get; set; }

    public string DurationType { get; set; } = "";
    public int? DurationAmount { get; set; }
    public string DurationUnit { get; set; } = "";
    public string DurationEnds { get; set; } = "";
    public bool RequiresConcentration { get; set; }

    public bool IsRitual { get; set; }
    public bool IsPrepared { get; set; }
    public bool IsFavorite { get; set; }

    public string Description { get; set; } = "";
    public string UpCast { get; set; } = "";

    public List<string> SavingThrows { get; set; } = [];
    public List<string> AbilityChecks { get; set; } = [];
    public List<string> DamageTypes { get; set; } = [];
    public List<string> DamageResistances { get; set; } = [];
    public List<string> DamageImmunities { get; set; } = [];
    public List<string> DamageVulnerabilities { get; set; } = [];
    public List<string> ConditionsInflicted { get; set; } = [];
    public List<string> ConditionImmunities { get; set; } = [];
    public List<string> SpellAttackTypes { get; set; } = [];
    public List<string> AffectsCreatureTypes { get; set; } = [];
    public List<string> MiscTags { get; set; } = [];
    public List<string> AreaTags { get; set; } = [];
    public List<string> Aliases { get; set; } = [];

    public bool IsSrd { get; set; }
    public bool IsBasicRules { get; set; }
    public bool IsSrd2024 { get; set; }
    public bool IsBasicRules2024 { get; set; }
    public bool HasFluff { get; set; }
    public bool HasFluffImages { get; set; }
    public string RawJson { get; set; } = "";

    public string LevelDisplay => Level == 0 ? "Cantrip" : $"Level {Level}";

    public string CastingTimeDisplay
    {
        get
        {
            var value = CastingTimeNumber is null
                ? CastingTimeUnit
                : $"{CastingTimeNumber} {CastingTimeUnit}";

            return string.IsNullOrWhiteSpace(CastingTimeCondition)
                ? value
                : $"{value}, {CastingTimeCondition}";
        }
    }

    public string RangeDisplay
    {
        get
        {
            if (RangeDistanceAmount is not null)
            {
                return $"{RangeDistanceAmount} {RangeDistanceType}";
            }

            return string.IsNullOrWhiteSpace(RangeDistanceType)
                ? RangeType
                : RangeDistanceType;
        }
    }

    public string DurationDisplay
    {
        get
        {
            var value = DurationAmount is null
                ? DurationType
                : $"{DurationAmount} {DurationUnit}";

            return RequiresConcentration && !string.IsNullOrWhiteSpace(value)
                ? $"Concentration, {value}"
                : value;
        }
    }

    public string ComponentsDisplay
    {
        get
        {
            var parts = new List<string>();
            if (HasVerbalComponent) parts.Add("V");
            if (HasSomaticComponent) parts.Add("S");
            if (HasMaterialComponent) parts.Add("M");
            if (HasRoyaltyComponent) parts.Add("R");

            var value = string.Join(", ", parts);
            return HasMaterialComponent && !string.IsNullOrWhiteSpace(MaterialComponent)
                ? $"{value} ({MaterialComponent})"
                : value;
        }
    }

    public string MaterialCostDisplay
    {
        get
        {
            if (MaterialCostCopper is null)
            {
                return "";
            }

            if (MaterialCostCopper.Value % 100 == 0)
            {
                return $"{MaterialCostCopper.Value / 100} gp";
            }

            if (MaterialCostCopper.Value % 10 == 0)
            {
                return $"{MaterialCostCopper.Value / 10} sp";
            }

            return $"{MaterialCostCopper.Value} cp";
        }
    }
}
