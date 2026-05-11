namespace DigitalCharacterSheet.Models;

public sealed class ItemEffect
{
    public int InventoryItemId { get; set; }
    public int? ItemDefinitionId { get; set; }
    public string EffectType { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string TargetLabel { get; set; } = "";
    public int Value { get; set; }
    public string SourceName { get; set; } = "";
    public string Confidence { get; set; } = "High";

    public bool IsBonus => Value != 0;
    public string Label => EffectType switch
    {
        "ArmorClassBonus" => "AC",
        "SavingThrowBonus" => "Saves",
        "ConcentrationSavingThrowBonus" => "Concentration",
        "AbilityCheckBonus" => "Checks",
        "WeaponAttackBonus" => "Weapon Attack",
        "WeaponDamageBonus" => "Weapon Damage",
        "SpellAttackBonus" => "Spell Attack",
        "SpellSaveDcBonus" => "Spell DC",
        "SpellDamageBonus" => "Spell Damage",
        "ProficiencyBonus" => "Proficiency",
        _ => EffectType.Replace("Bonus", "")
    };
}
