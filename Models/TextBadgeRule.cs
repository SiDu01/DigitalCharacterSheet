namespace DigitalCharacterSheet.Models;

public sealed class TextBadgeRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phrase { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string Category { get; set; } = "Custom";
}
