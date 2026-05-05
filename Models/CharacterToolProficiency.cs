namespace DigitalCharacterSheet.Models;

public sealed class CharacterToolProficiency
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public bool IsProficient { get; set; }
    public string Notes { get; set; } = "";

    public static List<CharacterToolProficiency> CreateDefaults()
    {
        return
        [
            new() { Name = "Artisan's Tools" },
            new() { Name = "Disguise Kit" },
            new() { Name = "Forgery Kit" },
            new() { Name = "Gaming Set" },
            new() { Name = "Herbalism Kit" },
            new() { Name = "Musical Instrument" },
            new() { Name = "Navigator's Tools" },
            new() { Name = "Poisoner's Kit" },
            new() { Name = "Thieves' Tools" },
            new() { Name = "Vehicles (Land)" },
            new() { Name = "Vehicles (Water)" }
        ];
    }
}
