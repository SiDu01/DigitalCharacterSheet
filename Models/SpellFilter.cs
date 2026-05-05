namespace DigitalCharacterSheet.Models;

public sealed class SpellFilter
{
    public string SearchText { get; set; } = "";
    public int? Level { get; set; }
    public string School { get; set; } = "";
    public string Source { get; set; } = "";
    public bool? RequiresConcentration { get; set; }
    public bool? IsRitual { get; set; }
}
