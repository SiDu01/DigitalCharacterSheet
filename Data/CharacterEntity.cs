using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("Characters")]
public sealed class CharacterEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public int? RaceDefinitionId { get; set; }

    [Indexed]
    public int? SubraceDefinitionId { get; set; }

    public string RaceName { get; set; } = "";
    public string RaceChoicesJson { get; set; } = "";

    [Indexed]
    public int? BackgroundDefinitionId { get; set; }

    public string BackgroundName { get; set; } = "";
    public string BackgroundChoicesJson { get; set; } = "";
    public string FeatChoicesJson { get; set; } = "";

    [Indexed]
    public string ClassName { get; set; } = "";

    public int? Level { get; set; }
    public int Strength { get; set; } = 10;
    public int Dexterity { get; set; } = 10;
    public int Constitution { get; set; } = 10;
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;
    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int TemporaryHitPoints { get; set; }
    public string ConditionsJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
