using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("RaceDefinitions")]
public sealed class RaceDefinitionEntity
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
    public string SizeJson { get; set; } = "";
    public string SpeedJson { get; set; } = "";
    public string AbilityJson { get; set; } = "";
    public string LanguageProficienciesJson { get; set; } = "";
    public string TraitTagsJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}
