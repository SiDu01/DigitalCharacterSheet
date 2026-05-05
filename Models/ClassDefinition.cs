namespace DigitalCharacterSheet.Models;

public sealed class ClassDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Slug { get; set; } = "";
    public int? SpellcastingProgressionId { get; set; }
    public string RawJson { get; set; } = "";

    public string DisplayName => $"{Name} ({Source})";
}
