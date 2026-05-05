using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("BackgroundDefinitions")]
public sealed class BackgroundDefinitionEntity
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
    public string AbilityJson { get; set; } = "";
    public string FeatsJson { get; set; } = "";
    public string SkillProficienciesJson { get; set; } = "";
    public string ToolProficienciesJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}
