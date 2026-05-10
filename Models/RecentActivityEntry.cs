namespace DigitalCharacterSheet.Models;

public sealed class RecentActivityEntry
{
    public string EntryType { get; set; } = "";
    public int EntityId { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Route { get; set; } = "";
    public DateTime ViewedAtUtc { get; set; }
    public DateTime? BookmarkedAtUtc { get; set; }
}
