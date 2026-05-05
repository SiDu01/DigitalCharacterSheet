namespace DigitalCharacterSheet.Models;

public sealed class RaceDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }
    public string Slug { get; set; } = "";
    public string SizeJson { get; set; } = "";
    public string SpeedJson { get; set; } = "";
    public string AbilityJson { get; set; } = "";
    public string LanguageProficienciesJson { get; set; } = "";
    public string TraitTagsJson { get; set; } = "";
    public string RawJson { get; set; } = "";

    public string DisplayName => $"{Name} ({Source})";
}
