using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public static class CharacterAbilityChoiceService
{
    private static readonly string[] AbilityOrder = ["str", "dex", "con", "int", "wis", "cha"];
    public static IReadOnlyList<string> AllAbilityOptions => AbilityOrder;

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

        var modes = new List<IReadOnlyList<CharacterAbilityChoiceRequirement>>();
        var index = 0;
        foreach (var entry in ability.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("choose", out var choose))
            {
                continue;
            }

            var entryRequirements = BuildChooseRequirements(choose, sourceKey, sourceName, index).ToList();
            if (entryRequirements.Count == 0)
            {
                continue;
            }

            modes.Add(entryRequirements);
            index++;
        }

        var requirements = modes.SelectMany(mode => mode).ToList();
        AddStandardPlusTwoPlusOneMode(requirements, sourceKey, sourceName);
        return requirements;
    }

    public static string DescribeAbilityEntries(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "None";
        }

        using var document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("ability", out var ability) || ability.ValueKind != JsonValueKind.Array)
        {
            return "None";
        }

        var descriptions = new List<string>();
        foreach (var entry in ability.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (entry.TryGetProperty("choose", out var choose))
            {
                descriptions.Add(DescribeChoose(choose));
                continue;
            }

            var fixedBonuses = entry.EnumerateObject()
                .Where(property => IsAbilityCode(property.Name)
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out _))
                .Select(property => $"{FormatAbilityShortName(property.Name)} +{property.Value.GetInt32()}")
                .ToList();
            if (fixedBonuses.Count > 0)
            {
                descriptions.Add(string.Join(", ", fixedBonuses));
            }
        }

        if (descriptions.Count == 0)
        {
            return "None";
        }

        return descriptions.Count == 1
            ? descriptions[0]
            : string.Join(" | ", descriptions.Select((description, index) => $"Option {index + 1}: {description}"));
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
            var values = ReadChoiceValues(property.Value).ToList();
            selections[property.Name] = IsModeMarkerKey(property.Name) || IsUnrestrictedMarkerKey(property.Name)
                ? values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList()
                : values.Where(IsAbilityCode).ToList();
        }

        return selections;
    }

    public static void AddSelectedBonuses(
        Dictionary<string, int> abilityBonuses,
        IEnumerable<CharacterAbilityChoiceRequirement> requirements,
        IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        foreach (var requirement in GetActiveRequirements(requirements, selectedChoices))
        {
            if (!selectedChoices.TryGetValue(requirement.Key, out var selected))
            {
                continue;
            }

            foreach (var abilityCode in selected
                         .Where(abilityCode => requirement.Options.Contains(abilityCode, StringComparer.OrdinalIgnoreCase))
                         .Take(requirement.Count))
            {
                abilityBonuses[abilityCode] = abilityBonuses.GetValueOrDefault(abilityCode) + requirement.BonusPerSelection;
            }
        }
    }

    public static IReadOnlyList<CharacterAbilityChoiceRequirement> GetActiveRequirements(
        IEnumerable<CharacterAbilityChoiceRequirement> requirements,
        IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        return requirements
            .GroupBy(requirement => requirement.ModeGroupKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var selectedMode = ReadSelectedMode(group.Key, selectedChoices)
                    ?? group.FirstOrDefault(requirement => selectedChoices.TryGetValue(requirement.Key, out var values) && values.Count > 0)?.ModeKey
                    ?? group.First().ModeKey;
                return group
                    .Where(requirement => string.Equals(requirement.ModeKey, selectedMode, StringComparison.OrdinalIgnoreCase))
                    .Select(requirement => ApplyUnrestrictedOptions(requirement, selectedChoices));
            })
            .ToList();
    }

    public static string BuildModeMarkerKey(string modeGroupKey)
    {
        return $"{modeGroupKey}:mode";
    }

    public static string BuildUnrestrictedMarkerKey(string modeGroupKey)
    {
        return $"{modeGroupKey}:unrestricted";
    }

    public static string? ReadSelectedMode(string modeGroupKey, IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        return selectedChoices.TryGetValue(BuildModeMarkerKey(modeGroupKey), out var modeValues)
            ? modeValues.FirstOrDefault()
            : null;
    }

    public static bool ReadIsUnrestricted(string modeGroupKey, IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        return selectedChoices.TryGetValue(BuildUnrestrictedMarkerKey(modeGroupKey), out var values)
            && values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    public static CharacterAbilityChoiceRequirement ApplyUnrestrictedOptions(
        CharacterAbilityChoiceRequirement requirement,
        IReadOnlyDictionary<string, List<string>> selectedChoices)
    {
        return ReadIsUnrestricted(requirement.ModeGroupKey, selectedChoices)
            ? requirement with { Options = AbilityOrder }
            : requirement;
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

    public static string FormatAbilityShortName(string abilityCode)
    {
        return abilityCode.ToUpperInvariant();
    }

    private static IEnumerable<CharacterAbilityChoiceRequirement> BuildChooseRequirements(JsonElement choose, string sourceKey, string sourceName, int index)
    {
        var modeGroupKey = $"{sourceKey}:ability";
        var modeKey = $"{modeGroupKey}:mode:{index}";
        if (choose.TryGetProperty("weighted", out var weighted) && weighted.ValueKind == JsonValueKind.Object)
        {
            var options = ReadAbilityOptions(weighted).ToList();
            var weights = ReadWeights(weighted).ToList();
            var modeName = BuildWeightedModeName(weights);
            foreach (var group in weights.GroupBy(weight => weight).OrderByDescending(group => group.Key))
            {
                yield return new CharacterAbilityChoiceRequirement(
                    $"{sourceKey}:ability:{index}:weighted:{group.Key}",
                    sourceName,
                    modeGroupKey,
                    modeKey,
                    modeName,
                    options,
                    group.Count(),
                    group.Key,
                    false);
            }

            yield break;
        }

        var simpleOptions = ReadAbilityOptions(choose).ToList();
        if (simpleOptions.Count == 0)
        {
            yield break;
        }

        var amount = ReadAmount(choose);
        var count = ReadCount(choose, amount);
        yield return new CharacterAbilityChoiceRequirement(
            $"{sourceKey}:ability:{index}",
            sourceName,
            modeGroupKey,
            modeKey,
            HasExplicitCount(choose) ? $"{count} x +{amount}" : $"+{amount}",
            simpleOptions,
            HasExplicitCount(choose) ? count : 1,
            amount,
            false);
    }

    private static void AddStandardPlusTwoPlusOneMode(List<CharacterAbilityChoiceRequirement> requirements, string sourceKey, string sourceName)
    {
        if (requirements.Count == 0)
        {
            return;
        }

        var modeGroupKey = $"{sourceKey}:ability";
        if (requirements
            .GroupBy(requirement => requirement.ModeKey, StringComparer.OrdinalIgnoreCase)
            .Any(mode => mode.Any(requirement => requirement.BonusPerSelection == 2)
                && mode.Any(requirement => requirement.BonusPerSelection == 1)))
        {
            return;
        }

        var options = requirements
            .SelectMany(requirement => requirement.Options)
            .Where(IsAbilityCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (options.Count == 0)
        {
            return;
        }

        var modeKey = $"{modeGroupKey}:mode:plus2plus1";
        requirements.Add(new CharacterAbilityChoiceRequirement(
            $"{sourceKey}:ability:plus2plus1:2",
            sourceName,
            modeGroupKey,
            modeKey,
            "+2 & +1",
            options,
            1,
            2,
            false));
        requirements.Add(new CharacterAbilityChoiceRequirement(
            $"{sourceKey}:ability:plus2plus1:1",
            sourceName,
            modeGroupKey,
            modeKey,
            "+2 & +1",
            options,
            1,
            1,
            false));
    }

    private static string BuildWeightedModeName(IReadOnlyList<int> weights)
    {
        return string.Join(" & ", weights
            .GroupBy(weight => weight)
            .OrderByDescending(group => group.Key)
            .Select(group => group.Count() == 1 ? $"+{group.Key}" : $"{group.Count()} x +{group.Key}"));
    }

    private static string DescribeChoose(JsonElement choose)
    {
        if (choose.TryGetProperty("weighted", out var weighted) && weighted.ValueKind == JsonValueKind.Object)
        {
            var options = string.Join(", ", ReadAbilityOptions(weighted).Select(FormatAbilityShortName));
            var weights = ReadWeights(weighted).ToList();
            return string.Join(" and ", weights
                .GroupBy(weight => weight)
                .OrderByDescending(group => group.Key)
                .Select(group => group.Count() == 1
                    ? $"choose +{group.Key} from {options}"
                    : $"choose {group.Count()} x +{group.Key} from {options}"));
        }

        var bonus = ReadAmount(choose);
        var count = ReadCount(choose, bonus);
        var abilities = string.Join(", ", ReadAbilityOptions(choose).Select(FormatAbilityShortName));
        return count == 1
            ? $"choose +{bonus} from {abilities}"
            : $"choose {count} x +{bonus} from {abilities}";
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

    private static IEnumerable<int> ReadWeights(JsonElement choose)
    {
        if (!choose.TryGetProperty("weights", out var weights) || weights.ValueKind != JsonValueKind.Array)
        {
            yield return 1;
            yield break;
        }

        foreach (var weight in weights.EnumerateArray())
        {
            if (weight.ValueKind == JsonValueKind.Number && weight.TryGetInt32(out var parsed))
            {
                yield return Math.Max(1, parsed);
            }
        }
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

    private static bool IsModeMarkerKey(string key)
    {
        return key.EndsWith(":mode", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnrestrictedMarkerKey(string key)
    {
        return key.EndsWith(":unrestricted", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CharacterAbilityChoiceRequirement(
    string Key,
    string SourceName,
    string ModeGroupKey,
    string ModeKey,
    string ModeName,
    IReadOnlyList<string> Options,
    int Count,
    int BonusPerSelection,
    bool AllowsDuplicateSelection);
