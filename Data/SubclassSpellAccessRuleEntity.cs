using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SubclassSpellAccessRules")]
public sealed class SubclassSpellAccessRuleEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SpellId { get; set; }

    [Indexed]
    public int SubclassDefinitionId { get; set; }

    [Indexed]
    public string AccessSource { get; set; } = "";
}
