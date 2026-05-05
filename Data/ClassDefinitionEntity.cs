using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("ClassDefinitions")]
public sealed class ClassDefinitionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string Slug { get; set; } = "";

    [Indexed]
    public int? SpellcastingProgressionId { get; set; }

    public string RawJson { get; set; } = "";
}
