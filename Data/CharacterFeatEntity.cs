using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterFeats")]
public sealed class CharacterFeatEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public int FeatDefinitionId { get; set; }
}
