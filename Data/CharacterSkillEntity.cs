using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterSkills")]
public sealed class CharacterSkillEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    public bool IsProficient { get; set; }
    public string RollMode { get; set; } = "Normal";
    public string Notes { get; set; } = "";
}
