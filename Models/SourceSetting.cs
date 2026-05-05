namespace DigitalCharacterSheet.Models;

public sealed class SourceSetting
{
    public string SourceCode { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public int DisplayPriority { get; set; }
    public string DisplayName { get; set; } = "";
}
