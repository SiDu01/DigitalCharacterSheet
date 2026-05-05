using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterSavingThrows")]
public sealed class CharacterSavingThrowEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string AbilityCode { get; set; } = "";

    public bool IsProficient { get; set; }
    public string RollMode { get; set; } = "Normal";
    public string Notes { get; set; } = "";
}
