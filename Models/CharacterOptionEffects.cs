namespace DigitalCharacterSheet.Models;

public sealed class CharacterOptionEffects
{
    public Dictionary<string, int> AbilityBonuses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SavingThrowProficiencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SkillProficiencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ToolProficiencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LanguageProficiencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DeferredChoices { get; set; } = [];
    public List<CharacterOptionChoice> ChoiceHints { get; set; } = [];

    public bool HasChanges => AbilityBonuses.Count > 0 || SavingThrowProficiencies.Count > 0 || SkillProficiencies.Count > 0 || ToolProficiencies.Count > 0 || LanguageProficiencies.Count > 0;
}

public sealed record CharacterOptionChoice(string Category, string Source, int Count, string Description);
