using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("ClassSpellAccessRules")]
public sealed class ClassSpellAccessRuleEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SpellId { get; set; }

    [Indexed]
    public int ClassDefinitionId { get; set; }

    [Indexed]
    public string AccessSource { get; set; } = "";
}
