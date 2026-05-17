using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DigitalCharacterSheet.Services;

public static partial class AbilityRuleParser
{
    private static readonly IReadOnlyDictionary<string, string> AbilityCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "str",
        ["Dexterity"] = "dex",
        ["Constitution"] = "con",
        ["Intelligence"] = "int",
        ["Wisdom"] = "wis",
        ["Charisma"] = "cha"
    };

    public static string BuildAbilityJsonFromText(JsonElement root)
    {
        var entriesText = string.Join(" ", ExtractEntryText(root));
        return BuildAbilityJsonFromText(entriesText);
    }

    public static string BuildAbilityJsonFromText(string text)
    {
        return TryBuildAbilityJsonFromText(text, out var abilityJson) ? abilityJson : "";
    }

    public static bool TryBuildAbilityJsonFromText(string text, out string abilityJson)
    {
        abilityJson = "";
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("score", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var nodes = new JsonArray();
        foreach (Match match in FixedIncreaseRegex().Matches(CleanText(text)))
        {
            var abilityCode = ToAbilityCode(match.Groups["ability"].Value);
            var amount = ReadAmount(match.Groups["amount"].Value);
            if (string.IsNullOrWhiteSpace(abilityCode) || amount <= 0)
            {
                continue;
            }

            nodes.Add(new JsonObject { [abilityCode] = amount });
        }

        foreach (Match match in ChoiceIncreaseRegex().Matches(CleanText(text)))
        {
            var amount = ReadAmount(match.Groups["amount"].Value);
            var options = ExtractAbilityCodes(match.Groups["abilities"].Value).ToList();
            if (amount <= 0 || options.Count < 2)
            {
                continue;
            }

            nodes.Add(new JsonObject
            {
                ["choose"] = new JsonObject
                {
                    ["from"] = new JsonArray(options.Select(option => JsonValue.Create(option)).ToArray<JsonNode?>()),
                    ["amount"] = amount
                }
            });
        }

        if (nodes.Count == 0)
        {
            return false;
        }

        abilityJson = nodes.ToJsonString();
        return true;
    }

    public static string EnrichRawJsonWithAbility(JsonElement element, string abilityJson)
    {
        if (string.IsNullOrWhiteSpace(abilityJson) || element.TryGetProperty("ability", out _))
        {
            return element.GetRawText();
        }

        var node = JsonNode.Parse(element.GetRawText())?.AsObject();
        var abilityNode = JsonNode.Parse(abilityJson);
        if (node is null || abilityNode is null)
        {
            return element.GetRawText();
        }

        node["ability"] = abilityNode;
        return node.ToJsonString();
    }

    private static IEnumerable<string> ExtractEntryText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "entries", "items", "entry" })
            {
                if (!element.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                foreach (var value in ExtractEntryText(property))
                {
                    yield return value;
                }
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in ExtractEntryText(item))
                {
                    yield return value;
                }
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ExtractAbilityCodes(string text)
    {
        foreach (Match match in AbilityNameRegex().Matches(text))
        {
            var code = ToAbilityCode(match.Value);
            if (!string.IsNullOrWhiteSpace(code))
            {
                yield return code;
            }
        }
    }

    private static string ToAbilityCode(string abilityName)
    {
        return AbilityCodes.TryGetValue(abilityName.Trim(), out var code) ? code : "";
    }

    private static int ReadAmount(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string CleanText(string text)
    {
        return MarkupRegex().Replace(text, "$1").Replace("}", "").Replace("{", "");
    }

    [GeneratedRegex(@"\b(?:your\s+)?(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+score\s+increases?\s+by\s+(?<amount>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FixedIncreaseRegex();

    [GeneratedRegex(@"\b(?:increase|increases?)\s+(?:your\s+)?(?<abilities>(?:(?:Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)(?:\s*,\s*|\s+or\s+|\s+and\s+)?){2,})\s+score\s+by\s+(?<amount>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChoiceIncreaseRegex();

    [GeneratedRegex(@"\b(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityNameRegex();

    [GeneratedRegex(@"\{@[a-zA-Z0-9_]+\s+([^|}]+)(?:\|[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupRegex();
}
