using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterFightingStyles")]
public sealed class CharacterFightingStyleEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
}
