using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterLanguageProficiencies")]
public sealed class CharacterLanguageProficiencyEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    public bool IsProficient { get; set; }
    public string Notes { get; set; } = "";
}
