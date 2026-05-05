using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SubclassDefinitions")]
public sealed class SubclassDefinitionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClassDefinitionId { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string Slug { get; set; } = "";

    public int? SpellcastingProgressionId { get; set; }

    public string RawJson { get; set; } = "";
}
