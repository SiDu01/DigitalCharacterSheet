using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("FeatDefinitions")]
public sealed class FeatDefinitionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Unique]
    public string Slug { get; set; } = "";

    public int? Page { get; set; }
    public string Category { get; set; } = "";
    public string PrerequisiteJson { get; set; } = "";
    public string AdditionalSpellsJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}
