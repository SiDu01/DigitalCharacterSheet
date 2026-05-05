using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SourceSettings")]
public sealed class SourceSettingEntity
{
    [PrimaryKey]
    public string SourceCode { get; set; } = "";

    [Indexed]
    public bool IsEnabled { get; set; } = true;

    [Indexed]
    public int DisplayPriority { get; set; }

    public string DisplayName { get; set; } = "";
}
