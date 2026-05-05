namespace DigitalCharacterSheet.Models;

public sealed class CharacterSavingThrow
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string AbilityCode { get; set; } = "";
    public string AbilityName { get; set; } = "";
    public bool IsProficient { get; set; }
    public string RollMode { get; set; } = "Normal";
    public string Notes { get; set; } = "";

    public static List<CharacterSavingThrow> CreateDefaults()
    {
        return
        [
            new() { AbilityCode = "str", AbilityName = "Strength" },
            new() { AbilityCode = "dex", AbilityName = "Dexterity" },
            new() { AbilityCode = "con", AbilityName = "Constitution" },
            new() { AbilityCode = "int", AbilityName = "Intelligence" },
            new() { AbilityCode = "wis", AbilityName = "Wisdom" },
            new() { AbilityCode = "cha", AbilityName = "Charisma" }
        ];
    }
}
