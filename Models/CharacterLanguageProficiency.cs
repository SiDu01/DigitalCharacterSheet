namespace DigitalCharacterSheet.Models;

public sealed class CharacterLanguageProficiency
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public bool IsProficient { get; set; }
    public string Notes { get; set; } = "";

    public static List<CharacterLanguageProficiency> CreateDefaults()
    {
        return
        [
            new() { Name = "Common" },
            new() { Name = "Dwarvish" },
            new() { Name = "Elvish" },
            new() { Name = "Giant" },
            new() { Name = "Gnomish" },
            new() { Name = "Goblin" },
            new() { Name = "Halfling" },
            new() { Name = "Orc" },
            new() { Name = "Abyssal" },
            new() { Name = "Celestial" },
            new() { Name = "Draconic" },
            new() { Name = "Deep Speech" },
            new() { Name = "Infernal" },
            new() { Name = "Primordial" },
            new() { Name = "Sylvan" },
            new() { Name = "Undercommon" }
        ];
    }
}
