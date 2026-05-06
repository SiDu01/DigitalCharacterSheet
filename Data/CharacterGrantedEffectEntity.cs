using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterGrantedEffects")]
public sealed class CharacterGrantedEffectEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string SourceType { get; set; } = "";

    [Indexed]
    public int SourceDefinitionId { get; set; }

    public int? SourceLevel { get; set; }

    [Indexed]
    public string EffectType { get; set; } = "";

    [Indexed]
    public string TargetKey { get; set; } = "";

    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
