using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("WikiEntries")]
public sealed class WikiEntryEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Category { get; set; } = "";

    [Indexed]
    public string Name { get; set; } = "";

    [Indexed]
    public string Source { get; set; } = "";

    [Indexed(Unique = true)]
    public string Slug { get; set; } = "";

    public int? Page { get; set; }
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public string EntriesJson { get; set; } = "";
    public string RawJson { get; set; } = "";
}
