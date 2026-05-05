namespace DigitalCharacterSheet.Models;

public sealed class SubraceDefinition
{
    public int Id { get; set; }
    public int? RaceDefinitionId { get; set; }
    public string RaceName { get; set; } = "";
    public string RaceSource { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? Page { get; set; }
    public string Slug { get; set; } = "";
    public string RawJson { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Source) ? Name : $"{Name} ({Source})";
}
