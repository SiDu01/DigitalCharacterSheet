using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterClasses")]
public sealed class CharacterClassEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public int ClassDefinitionId { get; set; }

    public int? SubclassDefinitionId { get; set; }
    public int Level { get; set; }
}
