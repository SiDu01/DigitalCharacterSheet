using System.Text.Json;
using DigitalCharacterSheet.Models;
using Microsoft.Maui.Storage;

namespace DigitalCharacterSheet.Services;

public sealed class TextBadgeSettingsService
{
    private const string PreferenceKey = "digitalCharacterSheet.textBadgeRules";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private List<TextBadgeRule>? _rules;

    public event Action? Changed;

    public IReadOnlyList<TextBadgeRule> GetRules()
    {
        EnsureLoaded();
        return _rules!;
    }

    public IReadOnlyList<string> GetEnabledPhrases()
    {
        EnsureLoaded();
        return _rules!
            .Where(rule => rule.IsEnabled && !string.IsNullOrWhiteSpace(rule.Phrase))
            .Select(rule => rule.Phrase.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(phrase => phrase.Length)
            .ToList();
    }

    public void SetEnabled(string id, bool isEnabled)
    {
        EnsureLoaded();
        var rule = _rules!.FirstOrDefault(rule => rule.Id == id);
        if (rule is null)
        {
            return;
        }

        rule.IsEnabled = isEnabled;
        Save();
    }

    public bool AddRule(string phrase)
    {
        EnsureLoaded();
        var cleaned = phrase.Trim();
        if (string.IsNullOrWhiteSpace(cleaned)
            || _rules!.Any(rule => string.Equals(rule.Phrase, cleaned, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _rules!.Add(new TextBadgeRule
        {
            Phrase = cleaned,
            Category = "Custom",
            IsEnabled = true
        });
        SortRules();
        Save();
        return true;
    }

    public void DeleteRule(string id)
    {
        EnsureLoaded();
        var removed = _rules!.RemoveAll(rule => rule.Id == id && rule.Category == "Custom");
        if (removed > 0)
        {
            Save();
        }
    }

    public void ResetDefaults()
    {
        _rules = BuildDefaultRules();
        Save();
    }

    private void EnsureLoaded()
    {
        if (_rules is not null)
        {
            return;
        }

        var json = Preferences.Default.Get(PreferenceKey, "");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _rules = JsonSerializer.Deserialize<List<TextBadgeRule>>(json, _jsonOptions);
            }
            catch (JsonException)
            {
                _rules = null;
            }
        }

        _rules ??= BuildDefaultRules();
        MergeMissingDefaults();
        SortRules();
    }

    private void MergeMissingDefaults()
    {
        var defaults = BuildDefaultRules();
        foreach (var defaultRule in defaults)
        {
            if (_rules!.Any(rule => string.Equals(rule.Phrase, defaultRule.Phrase, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _rules!.Add(defaultRule);
        }
    }

    private void SortRules()
    {
        _rules = _rules!
            .OrderBy(rule => rule.Category == "Custom" ? 1 : 0)
            .ThenBy(rule => rule.Category)
            .ThenBy(rule => rule.Phrase)
            .ToList();
    }

    private void Save()
    {
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(_rules, _jsonOptions));
        Changed?.Invoke();
    }

    private static List<TextBadgeRule> BuildDefaultRules()
    {
        return
        [
            Rule("Action", "Action Economy"),
            Rule("Bonus Action", "Action Economy"),
            Rule("Reaction", "Action Economy"),
            Rule("Long Rest", "Rest"),
            Rule("Short Rest", "Rest"),
            Rule("Armor Class", "Combat"),
            Rule("Attack Roll", "Combat"),
            Rule("Initiative", "Combat"),
            Rule("Saving Throw", "Combat"),
            Rule("Spell Save DC", "Combat"),
            Rule("Ability Check", "Checks"),
            Rule("Advantage", "Checks"),
            Rule("Disadvantage", "Checks"),
            Rule("Expertise", "Proficiency"),
            Rule("Half Proficiency", "Proficiency"),
            Rule("Proficiency Bonus", "Proficiency"),
            Rule("5 ft", "Distance"),
            Rule("10 ft", "Distance"),
            Rule("15 ft", "Distance"),
            Rule("20 ft", "Distance"),
            Rule("30 ft", "Distance"),
            Rule("60 ft", "Distance"),
            Rule("120 ft", "Distance"),
            Rule("Concentration", "Magic"),
            Rule("Teleport", "Magic"),
            Rule("Teleported", "Magic"),
            Rule("Temporary Hit Points", "Health"),
            Rule("Hit Points", "Health"),
            Rule("Resistance", "Defense"),
            Rule("Immunity", "Defense")
        ];
    }

    private static TextBadgeRule Rule(string phrase, string category)
    {
        return new TextBadgeRule
        {
            Id = CreateStableId(phrase),
            Phrase = phrase,
            Category = category,
            IsEnabled = true
        };
    }

    private static string CreateStableId(string phrase)
    {
        return phrase.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
    }
}
