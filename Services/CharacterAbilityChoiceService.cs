using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public static class CharacterAbilityChoiceService
{
    private static readonly string[] AbilityOrder = ["str", "dex", "con", "int", "wis", "cha"];

    public static IReadOnlyList<CharacterAbilityChoiceRequirement> BuildRequirements(string? rawJson, string sourceKey, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("ability", out var ability) || ability.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var requirements = new List<CharacterAbilityChoiceRequirement>();
        var index = 0;
        foreach (var entry in ability.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("choose", out var choose))
            {
                continue;
            }

            var options = ReadAbilityOptions(choose).ToList();
            if (options.Count == 0)
            {
                continue;
            }

            var amount = ReadAmount(choose);
            var count = ReadCount(choose, amount);
            requirements.Add(new CharacterAbilityChoiceRequirement(
                $"{sourceKey}:ability:{index}",
                sourceName,
                options,
                count,
                amount > 1 && !HasExplicitCount(choose) ? 1 : amount,
                amount > 1 && !HasExplicitCount(choose)));
            index++;
        }

        return requirements;
    }

    public static IReadOnlyDictionary<string, List<string>> ReadSelectedChoices(string choicesJson)
    {
        if (string.IsNullOrWhiteSpace(choicesJson))
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(choicesJson);
        if (!document.RootElement.TryGetProperty("abilityChoices", out var abilityChoices)
            || abilityChoices.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var selections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in abilityChoices.EnumerateObject())
        {
            selections[property.Name] = ReadChoiceValues(property.Value)
                .Where(IsAbilityCode)
                .ToList();
        }

        return selections;
    }

    public static void AddSelectedBonuses(
        Dictionary<string, int> abilityBonuses,
        IEnumerable<CharacterAbilityChoiceRequirement> requirements,
        IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        foreach (var requirement in requirements)
        {
            if (!selectedChoices.TryGetValue(requirement.Key, out var selected))
            {
                continue;
            }

            foreach (var abilityCode in selected
                         .Where(requirement.Options.Contains)
                         .Take(requirement.Count))
            {
                abilityBonuses[abilityCode] = abilityBonuses.GetValueOrDefault(abilityCode) + requirement.BonusPerSelection;
            }
        }
    }

    public static string FormatAbilityName(string abilityCode)
    {
        return abilityCode.ToLowerInvariant() switch
        {
            "str" => "Strength",
            "dex" => "Dexterity",
            "con" => "Constitution",
            "int" => "Intelligence",
            "wis" => "Wisdom",
            "cha" => "Charisma",
            _ => abilityCode
        };
    }

    private static IEnumerable<string> ReadAbilityOptions(JsonElement choose)
    {
        if (!choose.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Array)
        {
            return AbilityOrder;
        }

        return from.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? "")
            .Where(IsAbilityCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ReadAmount(JsonElement choose)
    {
        return choose.TryGetProperty("amount", out var amount)
            && amount.ValueKind == JsonValueKind.Number
            && amount.TryGetInt32(out var parsed)
            ? Math.Max(1, parsed)
            : 1;
    }

    private static int ReadCount(JsonElement choose, int amount)
    {
        return choose.TryGetProperty("count", out var count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt32(out var parsed)
            ? Math.Max(1, parsed)
            : Math.Max(1, amount);
    }

    private static bool HasExplicitCount(JsonElement choose)
    {
        return choose.TryGetProperty("count", out _);
    }

    private static IEnumerable<string> ReadChoiceValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString() ?? "";
            yield break;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var child in element.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.String)
            {
                yield return child.GetString() ?? "";
            }
        }
    }

    private static bool IsAbilityCode(string value)
    {
        return value.ToLowerInvariant() is "str" or "dex" or "con" or "int" or "wis" or "cha";
    }
}

public sealed record CharacterAbilityChoiceRequirement(
    string Key,
    string SourceName,
    IReadOnlyList<string> Options,
    int Count,
    int BonusPerSelection,
    bool AllowsDuplicateSelection);
