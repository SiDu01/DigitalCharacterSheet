namespace DigitalCharacterSheet.Models;

public sealed class SpellEligibility
{
    public int SpellId { get; set; }
    public List<string> Reasons { get; set; } = [];

    public string ReasonDisplay => string.Join(", ", Reasons.Distinct());
}
