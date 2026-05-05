using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SpellTags")]
public sealed class SpellTagEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SpellId { get; set; }

    [Indexed]
    public string Category { get; set; } = "";

    [Indexed]
    public string Value { get; set; } = "";
}
