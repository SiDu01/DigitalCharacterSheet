namespace DigitalCharacterSheet.Models;

public sealed class CharacterFeat
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int FeatDefinitionId { get; set; }
    public string FeatName { get; set; } = "";
    public string FeatSource { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(FeatSource) ? FeatName : $"{FeatName} ({FeatSource})";
}
