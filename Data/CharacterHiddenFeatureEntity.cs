using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterHiddenFeatures")]
public sealed class CharacterHiddenFeatureEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string FeatureKey { get; set; } = "";
}
