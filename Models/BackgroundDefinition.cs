namespace DigitalCharacterSheet.Models;

public sealed class BackgroundDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }
    public string Slug { get; set; } = "";
    public string AbilityJson { get; set; } = "";
    public string FeatsJson { get; set; } = "";
    public string SkillProficienciesJson { get; set; } = "";
    public string ToolProficienciesJson { get; set; } = "";
    public string RawJson { get; set; } = "";

    public string DisplayName => $"{Name} ({Source})";
}
