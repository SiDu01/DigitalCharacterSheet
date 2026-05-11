using System.Text.Json;
using DigitalCharacterSheet.Models;

namespace DigitalCharacterSheet.Services;

public static class ItemEffectService
{
    private static readonly IReadOnlyDictionary<string, string> BonusEffectTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ArmorClass"] = "ArmorClassBonus",
        ["SavingThrow"] = "SavingThrowBonus",
        ["ConcentrationSavingThrow"] = "ConcentrationSavingThrowBonus",
        ["AbilityCheck"] = "AbilityCheckBonus",
        ["Weapon"] = "WeaponAttackBonus",
        ["WeaponAttack"] = "WeaponAttackBonus",
        ["WeaponDamage"] = "WeaponDamageBonus",
        ["SpellAttack"] = "SpellAttackBonus",
        ["SpellSaveDc"] = "SpellSaveDcBonus",
        ["SpellDamage"] = "SpellDamageBonus",
        ["ProficiencyBonus"] = "ProficiencyBonus"
    };

    public static IReadOnlyList<ItemEffect> BuildActiveEffects(IEnumerable<CharacterInventoryItem> inventoryItems)
    {
        return inventoryItems
            .Where(IsActive)
            .SelectMany(BuildEffects)
            .ToList();
    }

    public static bool IsActive(CharacterInventoryItem item)
    {
        if (!item.IsCarried || item.ItemDefinition is null)
        {
            return false;
        }

        return item.RequiresAttunement ? item.IsAttuned : item.IsEquipped;
    }

    public static int GetDefinitionBonus(ItemDefinition item, params string[] bonusTypes)
    {
        return bonusTypes
            .Select(bonusType => item.Bonuses.FirstOrDefault(bonus => string.Equals(bonus.BonusType, bonusType, StringComparison.OrdinalIgnoreCase))?.Value)
            .Select(ParseSignedBonus)
            .FirstOrDefault(value => value != 0);
    }

    private static IEnumerable<ItemEffect> BuildEffects(CharacterInventoryItem inventoryItem)
    {
        var item = inventoryItem.ItemDefinition;
        if (item is null)
        {
            yield break;
        }

        foreach (var bonus in item.Bonuses)
        {
            if (!BonusEffectTypes.TryGetValue(bonus.BonusType, out var effectType))
            {
                continue;
            }

            var value = ParseSignedBonus(bonus.Value);
            if (value == 0)
            {
                continue;
            }

            yield return BuildEffect(inventoryItem, effectType, "", "", value, "Structured");
        }

        if (string.IsNullOrWhiteSpace(item.RawJson))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(item.RawJson);
        foreach (var target in ReadRuleEffectTargets(document.RootElement, "resist"))
        {
            yield return BuildEffect(inventoryItem, "Resistance", NormalizeRuleTarget(target), FormatRuleTarget(target), 0, "Structured");
        }

        foreach (var target in ReadRuleEffectTargets(document.RootElement, "immune"))
        {
            yield return BuildEffect(inventoryItem, "Immunity", NormalizeRuleTarget(target), FormatRuleTarget(target), 0, "Structured");
        }

        foreach (var target in ReadRuleEffectTargets(document.RootElement, "conditionImmune"))
        {
            yield return BuildEffect(inventoryItem, "Condition Immunity", NormalizeRuleTarget(target), FormatRuleTarget(target), 0, "Structured");
        }

        foreach (var target in ReadRuleEffectTargets(document.RootElement, "vulnerable"))
        {
            yield return BuildEffect(inventoryItem, "Vulnerability", NormalizeRuleTarget(target), FormatRuleTarget(target), 0, "Structured");
        }
    }

    private static ItemEffect BuildEffect(
        CharacterInventoryItem inventoryItem,
        string effectType,
        string targetKey,
        string targetLabel,
        int value,
        string confidence)
    {
        return new ItemEffect
        {
            InventoryItemId = inventoryItem.Id,
            ItemDefinitionId = inventoryItem.ItemDefinitionId,
            EffectType = effectType,
            TargetKey = targetKey,
            TargetLabel = targetLabel,
            Value = value,
            SourceName = inventoryItem.DisplayName,
            Confidence = confidence
        };
    }

    private static IEnumerable<string> ReadRuleEffectTargets(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            yield break;
        }

        foreach (var target in ReadRuleEffectTargetElement(property))
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                yield return target;
            }
        }
    }

    private static IEnumerable<string> ReadRuleEffectTargetElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? "";
                yield break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    foreach (var target in ReadRuleEffectTargetElement(child))
                    {
                        yield return target;
                    }
                }

                yield break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("choose"))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.True)
                    {
                        yield return property.Name;
                    }
                    else
                    {
                        foreach (var target in ReadRuleEffectTargetElement(property.Value))
                        {
                            yield return target;
                        }
                    }
                }

                yield break;
        }
    }

    private static string NormalizeRuleTarget(string target)
    {
        return CleanMarkup(target).Trim().ToLowerInvariant();
    }

    private static string FormatRuleTarget(string target)
    {
        var cleaned = CleanMarkup(target).Trim();
        return string.IsNullOrWhiteSpace(cleaned)
            ? "Unknown"
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.Replace("_", " "));
    }

    private static string CleanMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var cleaned = value.Replace("{@dc ", "DC ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"\{@[a-zA-Z0-9_]+\s+([^|}]+)(?:\|[^}]*)?\}",
            "$1");
        return cleaned.Replace("}", "").Replace("{", "");
    }

    private static int ParseSignedBonus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var cleaned = value.Trim().Replace("+", "");
        return int.TryParse(cleaned, out var parsed) ? parsed : 0;
    }
}
