namespace DigitalCharacterSheet.Models;

public sealed class SpellSlot
{
    public int SpellLevel { get; set; }
    public int MaxSlots { get; set; }
    public int UsedSlots { get; set; }
    public int RemainingSlots => Math.Max(0, MaxSlots - UsedSlots);
}
