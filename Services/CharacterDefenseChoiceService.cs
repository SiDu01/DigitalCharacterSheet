using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public static class CharacterDefenseChoiceService
{
    public static IReadOnlyList<CharacterDefenseChoiceRequirement> BuildRequirements(string? rawJson, string sourceKey, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(rawJson);
        var requirements = new List<CharacterDefenseChoiceRequirement>();
        AddRequirements(requirements, document.RootElement, "resist", "Resistance", sourceKey, sourceName);
        AddRequirements(requirements, document.RootElement, "immune", "Immunity", sourceKey, sourceName);
        AddRequirements(requirements, document.RootElement, "conditionImmune", "Condition Immunity", sourceKey, sourceName);
        AddRequirements(requirements, document.RootElement, "vulnerable", "Vulnerability", sourceKey, sourceName);
        return requirements;
    }

    public static IReadOnlyDictionary<string, List<string>> ReadSelectedChoices(string choicesJson)
    {
        if (string.IsNullOrWhiteSpace(choicesJson))
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(choicesJson);
        if (!document.RootElement.TryGetProperty("defenseChoices", out var defenseChoices)
            || defenseChoices.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var selections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in defenseChoices.EnumerateObject())
        {
            selections[property.Name] = ReadChoiceValues(property.Value).ToList();
        }

        return selections;
    }

    private static void AddRequirements(
        ICollection<CharacterDefenseChoiceRequirement> requirements,
        JsonElement root,
        string propertyName,
        string effectType,
        string sourceKey,
        string sourceName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        var index = 0;
        foreach (var choose in EnumerateChooseElements(property))
        {
            var options = ReadChoiceFromValues(choose).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (options.Count == 0)
            {
                continue;
            }

            var count = ReadChoiceCount(choose);
            requirements.Add(new CharacterDefenseChoiceRequirement(
                $"{sourceKey}:{propertyName}:{index}",
                sourceName,
                effectType,
                options,
                count));
            index++;
        }
    }

    private static IEnumerable<JsonElement> EnumerateChooseElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("choose", out var choose))
        {
            yield return choose;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var childChoose in EnumerateChooseElements(child))
                {
                    yield return childChoose;
                }
            }
        }
    }

    private static IEnumerable<string> ReadChoiceFromValues(JsonElement choose)
    {
        if (!choose.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in from.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return value.GetString() ?? "";
            }
        }
    }

    private static int ReadChoiceCount(JsonElement choose)
    {
        return choose.TryGetProperty("count", out var count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt32(out var parsed)
            ? Math.Max(1, parsed)
            : 1;
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
}

public sealed record CharacterDefenseChoiceRequirement(
    string Key,
    string SourceName,
    string EffectType,
    IReadOnlyList<string> Options,
    int Count);
