namespace DigitalCharacterSheet.Models;

public sealed class CharacterInventoryItem
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int? ItemDefinitionId { get; set; }
    public ItemDefinition? ItemDefinition { get; set; }
    public string CustomName { get; set; } = "";
    public string CustomDescription { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public bool IsEquipped { get; set; }
    public bool IsAttuned { get; set; }
    public bool IsCarried { get; set; } = true;
    public string ContainerName { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? CurrentCharges { get; set; }
    public int? MaxCharges { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(CustomName)
        ? CustomName
        : ItemDefinition?.Name ?? "Custom item";

    public string DetailText => !string.IsNullOrWhiteSpace(CustomDescription)
        ? CustomDescription
        : ItemDefinition?.Description ?? "";

    public bool RequiresAttunement => ItemDefinition?.RequiresAttunement == true;
    public string Source => ItemDefinition?.Source ?? "";
    public string Rarity => ItemDefinition?.Rarity ?? "";
    public string Category => ItemDefinition?.Category ?? "Custom";
    public string TypeCode => ItemDefinition?.TypeCode ?? "";
}
