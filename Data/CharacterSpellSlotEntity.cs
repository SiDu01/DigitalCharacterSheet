using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterSpellSlots")]
public sealed class CharacterSpellSlotEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public int SpellLevel { get; set; }

    public int UsedSlots { get; set; }
}
