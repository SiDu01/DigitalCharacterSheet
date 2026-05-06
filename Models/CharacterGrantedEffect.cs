namespace DigitalCharacterSheet.Models;

public sealed class CharacterGrantedEffect
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string SourceType { get; set; } = "";
    public int SourceDefinitionId { get; set; }
    public int? SourceLevel { get; set; }
    public string EffectType { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
