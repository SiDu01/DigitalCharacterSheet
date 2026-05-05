using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterSpells")]
public sealed class CharacterSpellEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public int SpellId { get; set; }

    [Indexed]
    public string Mode { get; set; } = "Known";

    public DateTime AddedAt { get; set; }
}
