using System.Text.RegularExpressions;

namespace DigitalCharacterSheet.Services;

public static class DescriptionRuleMapper
{
    public static DescriptionRuleMapping MapFeature(string name, IEnumerable<string> paragraphs)
    {
        var rawText = string.Join(" ", paragraphs);
        var text = CleanText(rawText);
        var effects = new List<MappedRuleEffect>();
        var choices = new List<MappedRuleChoice>();

        AddInitiativeRules(effects, name, text);
        AddExpertiseRules(choices, name, text, rawText);
        AddHalfProficiencyRules(effects, name, text);
        AddToolExpertiseRules(effects, name, text);
        AddAdvantageRules(effects, text);
        AddMovementAndSenseRules(effects, text);
        AddSpellBonusRules(effects, text);
        AddProficiencyChoiceRules(choices, text);
        AddTemporaryHitPointRules(effects, text);

        return new DescriptionRuleMapping(effects, choices);
    }

    private static void AddInitiativeRules(ICollection<MappedRuleEffect> effects, string name, string text)
    {
        if (NameIs(name, "Temporal Awareness")
            || Contains(text, "add your Intelligence modifier to your initiative")
            || Contains(text, "bonus to your initiative rolls equal to your Intelligence modifier"))
        {
            effects.Add(new MappedRuleEffect("InitiativeAbilityBonus", "int", "Intelligence modifier to initiative", "TextPattern", "High"));
        }

        if (Contains(text, "bonus to your initiative rolls equal to your Wisdom modifier")
            || Contains(text, "add your Wisdom modifier to the roll"))
        {
            effects.Add(new MappedRuleEffect("InitiativeAbilityBonus", "wis", "Wisdom modifier to initiative", "TextPattern", "High"));
        }

        if (Contains(text, "bonus to your initiative rolls equal to your Charisma modifier"))
        {
            effects.Add(new MappedRuleEffect("InitiativeAbilityBonus", "cha", "Charisma modifier to initiative", "TextPattern", "High"));
        }

        if (Contains(text, "advantage on initiative rolls")
            || Contains(text, "advantage on initiative"))
        {
            effects.Add(new MappedRuleEffect("InitiativeAdvantage", "initiative", "Advantage on initiative", "TextPattern", "High"));
        }
    }

    private static void AddExpertiseRules(ICollection<MappedRuleChoice> choices, string name, string text, string rawText)
    {
        if (NameIs(name, "Expertise"))
        {
            choices.Add(new MappedRuleChoice("SkillExpertise", "ProficientSkills", ReadCountBeforeExpertise(text, 2), "Choose proficient skills for Expertise", "TextPattern", "Medium"));
            return;
        }

        var listedSkills = ExtractSkillReferences(rawText).ToList();
        if (Regex.IsMatch(text, @"choose (one|two|three|four|\d+) of the following skills in which you have proficiency.*expertise", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"choose (one|two|three|four|\d+) skill(?:s)? in which you have proficiency.*expertise", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"choose (?:a|one) skill in which you have proficiency.*expertise", RegexOptions.IgnoreCase))
        {
            choices.Add(new MappedRuleChoice(
                "SkillExpertise",
                listedSkills.Count > 0 ? BuildSkillListScope(listedSkills) : "ProficientSkills",
                ReadCountAfterChoose(text, 1),
                "Choose proficient skills for Expertise",
                "TextPattern",
                listedSkills.Count > 0 ? "High" : "Medium"));
            return;
        }

        if (Regex.IsMatch(text, @"\bexpertise in (one|two|three|four|\d+) (?:of your )?(?:skill|skills|skill proficiencies)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"choose (one|two|three|four|\d+) (?:more )?(?:of your )?skill proficiencies.*expertise", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"choose (one|two|three|four|\d+) (?:more )?of your proficiencies.*expertise", RegexOptions.IgnoreCase))
        {
            choices.Add(new MappedRuleChoice("SkillExpertise", "ProficientSkills", ReadCountBeforeExpertise(text, 1), "Choose proficient skills for Expertise", "TextPattern", "Medium"));
        }
    }

    private static void AddHalfProficiencyRules(ICollection<MappedRuleEffect> effects, string name, string text)
    {
        if (NameIs(name, "Jack of All Trades")
            || Contains(text, "half your Proficiency Bonus")
            || Contains(text, "half your proficiency bonus"))
        {
            effects.Add(new MappedRuleEffect("SkillHalfProficiency", "AllSkills", "Half proficiency on non-proficient skills", "TextPattern", "High"));
        }
    }

    private static void AddToolExpertiseRules(ICollection<MappedRuleEffect> effects, string name, string text)
    {
        if (NameIs(name, "Tool Expertise")
            || Contains(text, "your proficiency bonus is doubled for any ability check you make that uses your proficiency with a tool"))
        {
            effects.Add(new MappedRuleEffect("ToolExpertise", "ProficientTools", "Double proficiency with proficient tools", "TextPattern", "High"));
        }
    }

    private static void AddAdvantageRules(ICollection<MappedRuleEffect> effects, string text)
    {
        foreach (Match match in Regex.Matches(text, @"advantage on ([^.]+?)(?:\.|,|;)", RegexOptions.IgnoreCase))
        {
            var target = match.Groups[1].Value.Trim();
            if (target.Contains("saving throw", StringComparison.OrdinalIgnoreCase))
            {
                effects.Add(new MappedRuleEffect("AdvantageOnSavingThrow", NormalizeTarget(target), $"Advantage on {target}", "TextPattern", "Medium"));
                continue;
            }

            if (target.Contains("check", StringComparison.OrdinalIgnoreCase))
            {
                effects.Add(new MappedRuleEffect("AdvantageOnCheck", NormalizeTarget(target), $"Advantage on {target}", "TextPattern", "Medium"));
                continue;
            }

            if (target.Contains("attack roll", StringComparison.OrdinalIgnoreCase))
            {
                effects.Add(new MappedRuleEffect("AdvantageOnAttack", NormalizeTarget(target), $"Advantage on {target}", "TextPattern", "Medium"));
            }
        }

        foreach (Match match in Regex.Matches(text, @"disadvantage on ([^.]+?)(?:\.|,|;)", RegexOptions.IgnoreCase))
        {
            var target = match.Groups[1].Value.Trim();
            effects.Add(new MappedRuleEffect("DisadvantageOnRoll", NormalizeTarget(target), $"Disadvantage on {target}", "TextPattern", "Medium"));
        }
    }

    private static void AddMovementAndSenseRules(ICollection<MappedRuleEffect> effects, string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?:your speed increases by|increase your walking speed by) (\d+) feet", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("SpeedBonus", "walking", $"+{match.Groups[1].Value} ft. walking speed", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"(?:flying|fly) speed (?:equal to your walking speed|of (\d+) feet)", RegexOptions.IgnoreCase))
        {
            var value = string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "walking" : $"{match.Groups[1].Value} ft.";
            effects.Add(new MappedRuleEffect("MovementMode", "fly", $"Fly speed {value}", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"(?:swimming|swim) speed (?:equal to your walking speed|of (\d+) feet)", RegexOptions.IgnoreCase))
        {
            var value = string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "walking" : $"{match.Groups[1].Value} ft.";
            effects.Add(new MappedRuleEffect("MovementMode", "swim", $"Swim speed {value}", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"(?:climbing|climb) speed (?:equal to your walking speed|of (\d+) feet)", RegexOptions.IgnoreCase))
        {
            var value = string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "walking" : $"{match.Groups[1].Value} ft.";
            effects.Add(new MappedRuleEffect("MovementMode", "climb", $"Climb speed {value}", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"darkvision(?: with a range of| out to| to a range of)? (\d+) feet", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("Sense", "darkvision", $"Darkvision {match.Groups[1].Value} ft.", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"darkvision.*increases by (\d+) feet", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("SenseBonus", "darkvision", $"+{match.Groups[1].Value} ft. Darkvision", "TextPattern", "Medium"));
        }

        foreach (Match match in Regex.Matches(text, @"blindsight(?: out to| with a range of)? (\d+) feet", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("Sense", "blindsight", $"Blindsight {match.Groups[1].Value} ft.", "TextPattern", "High"));
        }
    }

    private static void AddSpellBonusRules(ICollection<MappedRuleEffect> effects, string text)
    {
        foreach (Match match in Regex.Matches(text, @"grants? a \+?(\d+) bonus to (?:any )?spell attack rolls? and spell (?:saving throw DCs|save DC)", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("SpellAttackBonus", "all", $"+{match.Groups[1].Value} spell attack", "TextPattern", "High"));
            effects.Add(new MappedRuleEffect("SpellSaveDcBonus", "all", $"+{match.Groups[1].Value} spell save DC", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"\+?(\d+) bonus to (?:your )?spell save DC", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("SpellSaveDcBonus", "all", $"+{match.Groups[1].Value} spell save DC", "TextPattern", "High"));
        }

        foreach (Match match in Regex.Matches(text, @"\+?(\d+) bonus to (?:your )?spell attack", RegexOptions.IgnoreCase))
        {
            effects.Add(new MappedRuleEffect("SpellAttackBonus", "all", $"+{match.Groups[1].Value} spell attack", "TextPattern", "High"));
        }
    }

    private static void AddProficiencyChoiceRules(ICollection<MappedRuleChoice> choices, string text)
    {
        foreach (Match match in Regex.Matches(text, @"gain proficiency in (one|two|three|four|\d+) skills? of your choice", RegexOptions.IgnoreCase))
        {
            choices.Add(new MappedRuleChoice("SkillProficiency", "AnySkill", ReadCountWord(match.Groups[1].Value, 1), "Choose skill proficiencies", "TextPattern", "Medium"));
        }

        foreach (Match match in Regex.Matches(text, @"gain proficiency with (one|two|three|four|\d+) (?:different )?(?:tools?|artisan's tools|musical instruments?)", RegexOptions.IgnoreCase))
        {
            choices.Add(new MappedRuleChoice("ToolProficiency", "AnyTool", ReadCountWord(match.Groups[1].Value, 1), "Choose tool proficiencies", "TextPattern", "Medium"));
        }

        foreach (Match match in Regex.Matches(text, @"learn (one|two|three|four|\d+) languages? of your choice", RegexOptions.IgnoreCase))
        {
            choices.Add(new MappedRuleChoice("LanguageProficiency", "AnyLanguage", ReadCountWord(match.Groups[1].Value, 1), "Choose languages", "TextPattern", "Medium"));
        }
    }

    private static void AddTemporaryHitPointRules(ICollection<MappedRuleEffect> effects, string text)
    {
        if (Contains(text, "temporary hit points equal to your proficiency bonus"))
        {
            effects.Add(new MappedRuleEffect("TemporaryHitPointsFormula", "proficiency", "Temporary HP equal to proficiency bonus", "TextPattern", "Medium"));
        }

        if (Contains(text, "temporary hit points equal to your level"))
        {
            effects.Add(new MappedRuleEffect("TemporaryHitPointsFormula", "level", "Temporary HP based on level", "TextPattern", "Medium"));
        }
    }

    private static int ReadCountBeforeExpertise(string text, int fallback)
    {
        var match = Regex.Match(text, @"(?:choose|in|gain)\s+(one|two|three|four|\d+)\s+(?:more )?(?:of your )?(?:skill|skills|skill proficiencies|proficiencies)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return fallback;
        }

        return ReadCountWord(match.Groups[1].Value, fallback);
    }

    private static int ReadCountAfterChoose(string text, int fallback)
    {
        var match = Regex.Match(text, @"choose\s+(?:a|an|one|two|three|four|\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return fallback;
        }

        var value = match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return value.Equals("a", StringComparison.OrdinalIgnoreCase) || value.Equals("an", StringComparison.OrdinalIgnoreCase)
            ? 1
            : ReadCountWord(value, fallback);
    }

    private static IEnumerable<string> ExtractSkillReferences(string rawText)
    {
        foreach (Match match in Regex.Matches(rawText, @"\{@skill\s+([^|}]+)", RegexOptions.IgnoreCase))
        {
            var skill = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(skill))
            {
                yield return skill;
            }
        }
    }

    private static string BuildSkillListScope(IEnumerable<string> skills)
    {
        return "SkillList:" + string.Join(";", skills.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static int ReadCountWord(string value, int fallback)
    {
        return value.ToLowerInvariant() switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            var number when int.TryParse(number, out var parsed) => Math.Max(1, parsed),
            _ => fallback
        };
    }

    private static string NormalizeTarget(string target)
    {
        return Regex.Replace(target.ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private static bool NameIs(string actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanText(string text)
    {
        return Regex.Replace(text, @"\{@[a-zA-Z]+ ([^|}]+)(?:\|[^}]*)?\}", "$1")
            .Replace("|XPHB", "", StringComparison.OrdinalIgnoreCase)
            .Replace("|PHB", "", StringComparison.OrdinalIgnoreCase)
            .Replace("}", "")
            .Trim();
    }
}

public sealed record DescriptionRuleMapping(IReadOnlyList<MappedRuleEffect> Effects, IReadOnlyList<MappedRuleChoice> Choices);

public sealed record MappedRuleEffect(string EffectType, string TargetKey, string Label, string Source = "TextPattern", string Confidence = "Medium");

public sealed record MappedRuleChoice(string ChoiceType, string TargetScope, int Count, string Label, string Source = "TextPattern", string Confidence = "Medium");
