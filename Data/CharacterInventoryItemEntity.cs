using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("CharacterInventoryItems")]
public sealed class CharacterInventoryItemEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CharacterId { get; set; }

    [Indexed]
    public int? ItemDefinitionId { get; set; }

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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
