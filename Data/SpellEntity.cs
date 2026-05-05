using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("Spells")]
public sealed class SpellEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed]
    public string SourceNameKey { get; set; } = "";

    public int? Page { get; set; }

    [Indexed]
    public int Level { get; set; }

    [Indexed]
    public string SchoolCode { get; set; } = "";

    [Indexed]
    public string School { get; set; } = "";

    public int? CastingTimeNumber { get; set; }

    [Indexed]
    public string CastingTimeUnit { get; set; } = "";

    public string CastingTimeCondition { get; set; } = "";
    public string CastingTimeNote { get; set; } = "";

    [Indexed]
    public string RangeType { get; set; } = "";

    [Indexed]
    public string RangeDistanceType { get; set; } = "";

    [Indexed]
    public int? RangeDistanceAmount { get; set; }

    public bool HasVerbalComponent { get; set; }
    public bool HasSomaticComponent { get; set; }
    public bool HasMaterialComponent { get; set; }
    public string MaterialComponent { get; set; } = "";

    [Indexed]
    public int? MaterialCostCopper { get; set; }

    public bool ConsumesMaterial { get; set; }
    public bool HasRoyaltyComponent { get; set; }

    [Indexed]
    public string DurationType { get; set; } = "";

    public int? DurationAmount { get; set; }
    public string DurationUnit { get; set; } = "";
    public string DurationEnds { get; set; } = "";

    [Indexed]
    public bool RequiresConcentration { get; set; }

    [Indexed]
    public bool IsRitual { get; set; }

    [Indexed]
    public bool IsPrepared { get; set; }

    [Indexed]
    public bool IsFavorite { get; set; }

    public string Description { get; set; } = "";
    public string UpCast { get; set; } = "";

    public bool IsSrd { get; set; }
    public bool IsBasicRules { get; set; }
    public bool IsSrd2024 { get; set; }
    public bool IsBasicRules2024 { get; set; }
    public bool HasFluff { get; set; }
    public bool HasFluffImages { get; set; }
    public string RawJson { get; set; } = "";
}
