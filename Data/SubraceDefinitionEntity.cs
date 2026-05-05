using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SubraceDefinitions")]
public sealed class SubraceDefinitionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int? RaceDefinitionId { get; set; }

    [Indexed]
    public string RaceName { get; set; } = "";

    [Indexed]
    public string RaceSource { get; set; } = "";

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Unique]
    public string Slug { get; set; } = "";

    public int? Page { get; set; }
    public string RawJson { get; set; } = "";
}
