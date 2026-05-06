namespace DigitalCharacterSheet.Models;

public sealed class CharacterFightingStyle
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Source) ? Name : $"{Name} ({Source})";
}
