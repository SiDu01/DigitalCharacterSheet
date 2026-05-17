namespace DigitalCharacterSheet.Models;

public sealed class FeatDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }
    public string Category { get; set; } = "";
    public string Slug { get; set; } = "";
    public string PrerequisiteJson { get; set; } = "";
    public string AdditionalSpellsJson { get; set; } = "";
    public string AbilityJson { get; set; } = "";
    public bool IsRepeatable { get; set; }
    public string RawJson { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Category)
        ? $"{Name} ({Source})"
        : $"{Name} ({Source}, {Category})";
}
