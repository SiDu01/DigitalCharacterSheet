using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("DatabaseMetadata")]
public sealed class DatabaseMetadata
{
    [PrimaryKey]
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
