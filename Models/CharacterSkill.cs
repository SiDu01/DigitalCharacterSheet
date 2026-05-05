namespace DigitalCharacterSheet.Models;

public sealed class CharacterSkill
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public string AbilityCode { get; set; } = "";
    public string AbilityName { get; set; } = "";
    public bool IsProficient { get; set; }
    public string RollMode { get; set; } = "Normal";
    public string Notes { get; set; } = "";

    public static List<CharacterSkill> CreateDefaults()
    {
        return
        [
            new() { Name = "Acrobatics", AbilityCode = "dex", AbilityName = "Dexterity" },
            new() { Name = "Animal Handling", AbilityCode = "wis", AbilityName = "Wisdom" },
            new() { Name = "Arcana", AbilityCode = "int", AbilityName = "Intelligence" },
            new() { Name = "Athletics", AbilityCode = "str", AbilityName = "Strength" },
            new() { Name = "Deception", AbilityCode = "cha", AbilityName = "Charisma" },
            new() { Name = "History", AbilityCode = "int", AbilityName = "Intelligence" },
            new() { Name = "Insight", AbilityCode = "wis", AbilityName = "Wisdom" },
            new() { Name = "Intimidation", AbilityCode = "cha", AbilityName = "Charisma" },
            new() { Name = "Investigation", AbilityCode = "int", AbilityName = "Intelligence" },
            new() { Name = "Medicine", AbilityCode = "wis", AbilityName = "Wisdom" },
            new() { Name = "Nature", AbilityCode = "int", AbilityName = "Intelligence" },
            new() { Name = "Perception", AbilityCode = "wis", AbilityName = "Wisdom" },
            new() { Name = "Performance", AbilityCode = "cha", AbilityName = "Charisma" },
            new() { Name = "Persuasion", AbilityCode = "cha", AbilityName = "Charisma" },
            new() { Name = "Religion", AbilityCode = "int", AbilityName = "Intelligence" },
            new() { Name = "Sleight of Hand", AbilityCode = "dex", AbilityName = "Dexterity" },
            new() { Name = "Stealth", AbilityCode = "dex", AbilityName = "Dexterity" },
            new() { Name = "Survival", AbilityCode = "wis", AbilityName = "Wisdom" }
        ];
    }
}
