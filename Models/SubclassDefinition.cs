namespace DigitalCharacterSheet.Models;

public sealed class SubclassDefinition
{
    public int Id { get; set; }
    public int ClassDefinitionId { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Slug { get; set; } = "";
    public string RawJson { get; set; } = "";

    public string DisplayName => $"{Name} ({Source})";
}
