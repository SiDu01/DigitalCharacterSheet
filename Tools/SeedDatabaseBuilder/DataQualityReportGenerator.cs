using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DigitalCharacterSheet.Services;

internal static partial class DataQualityReportGenerator
{
    private const int PreviewLength = 180;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> KnownEntryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "abilityAttackMod",
        "abilityDc",
        "attack",
        "bonus",
        "bonusSpeed",
        "bonusSpellAttack",
        "bonusSpellSaveDc",
        "cell",
        "dice",
        "entries",
        "entriesOtherSource",
        "gallery",
        "homebrew",
        "image",
        "inset",
        "insetReadaloud",
        "item",
        "itemSpell",
        "list",
        "options",
        "quote",
        "refClassFeature",
        "refOptionalfeature",
        "refSubclassFeature",
        "refTable",
        "section",
        "statblock",
        "table",
        "variant"
    };

    public static DataQualityReportResult WriteReports(string sourceDataPath, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);

        var context = new DataQualityContext(sourceDataPath);
        AnalyzeRootFile(context, "races.json", ("race", "race"), ("subrace", "race-version"));
        AnalyzeRootFile(context, "backgrounds.json", ("background", "background"));
        AnalyzeRootFile(context, "feats.json", ("feat", "feat"));
        AnalyzeRootFile(context, "items.json", ("item", "item"), ("itemGroup", "item-group"), ("baseitem", "base-item"));

        var classDirectory = Path.Combine(sourceDataPath, "class");
        if (Directory.Exists(classDirectory))
        {
            foreach (var classFile in Directory.EnumerateFiles(classDirectory, "*.json").OrderBy(path => path))
            {
                AnalyzeJsonFile(context, classFile, ("class", "class"), ("subclass", "subclass"));
            }
        }

        context.FinalizeDuplicateChecks();

        var markdownPath = Path.Combine(reportDirectory, "data-quality-report.md");
        var jsonPath = Path.Combine(reportDirectory, "unhandled-cases.json");
        var unhandledMarkdownPath = Path.Combine(reportDirectory, "unhandled-cases.md");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(context.Cases, JsonOptions), Encoding.UTF8);
        File.WriteAllText(markdownPath, BuildDataQualityMarkdown(context), Encoding.UTF8);
        File.WriteAllText(unhandledMarkdownPath, BuildUnhandledMarkdown(context.Cases), Encoding.UTF8);

        return new DataQualityReportResult(markdownPath, jsonPath, unhandledMarkdownPath, context.Cases.Count);
    }

    private static void AnalyzeRootFile(DataQualityContext context, string fileName, params (string PropertyName, string Category)[] rootArrays)
    {
        var path = Path.Combine(context.SourceDataPath, fileName);
        if (File.Exists(path))
        {
            AnalyzeJsonFile(context, path, rootArrays);
        }
        else
        {
            context.AddCase(new UnhandledCase(
                "source-data",
                fileName,
                "",
                fileName,
                "missing-source-file",
                "warning",
                null,
                $"Expected source file was not found: {fileName}",
                null,
                null));
        }
    }

    private static void AnalyzeJsonFile(DataQualityContext context, string filePath, params (string PropertyName, string Category)[] rootArrays)
    {
        using var stream = File.OpenRead(filePath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        var relativeFile = Path.GetRelativePath(context.SourceDataPath, filePath);

        foreach (var (propertyName, category) in rootArrays)
        {
            if (!document.RootElement.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var index = 0;
            foreach (var element in array.EnumerateArray())
            {
                var name = ReadString(element, "name");
                var source = ReadString(element, "source");
                var path = $"{relativeFile}.{propertyName}[{index}]";
                context.Count(category);
                context.TrackDuplicate(category, name, source, path);
                AnalyzeEntityShape(context, category, name, source, path, element);
                ScanElement(context, category, name, source, path, element);
                index++;
            }
        }
    }

    private static void AnalyzeEntityShape(DataQualityContext context, string category, string name, string source, string path, JsonElement element)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (!IsUnnamedRaceOverlay(category, source, element))
            {
                context.AddCase(new UnhandledCase(category, name, source, path, "missing-name", "error", null, "Entity has no name.", null, null));
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            context.AddCase(new UnhandledCase(category, name, source, path, "missing-source", "warning", null, "Entity has no source.", null, null));
        }

        if (category is "race-version")
        {
            var raceName = ReadString(element, "raceName");
            var raceSource = ReadString(element, "raceSource");
            if ((string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(raceSource))
                && element.TryGetProperty("_copy", out var copy)
                && copy.ValueKind == JsonValueKind.Object)
            {
                raceName = ReadString(copy, "raceName");
                raceSource = ReadString(copy, "raceSource");
            }

            if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(raceSource))
            {
                context.AddCase(new UnhandledCase(category, name, source, path, "missing-parent-link", "warning", null, "Race version is missing raceName or raceSource.", null, "RaceVersionParentParser"));
            }
        }

        if (category is "subclass")
        {
            var className = ReadString(element, "className");
            var classSource = ReadString(element, "classSource");
            if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(classSource))
            {
                context.AddCase(new UnhandledCase(category, name, source, path, "missing-parent-link", "warning", null, "Subclass is missing className or classSource.", null, "SubclassParentParser"));
            }
        }

        if (category is "class")
        {
            AnalyzeClassSubclassGrantLevels(context, category, name, source, path, element);
        }
    }

    private static void AnalyzeClassSubclassGrantLevels(DataQualityContext context, string category, string name, string source, string path, JsonElement element)
    {
        if (ReadBool(element, "isSidekick"))
        {
            return;
        }

        if (!element.TryGetProperty("classFeatures", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            if (element.TryGetProperty("advancement", out var advancement)
                && advancement.ValueKind == JsonValueKind.Array
                && path.Contains("foundry.json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            context.AddCase(new UnhandledCase(category, name, source, path, "missing-class-features", "warning", null, "Class has no classFeatures array.", null, "ClassFeatureParser"));
            return;
        }

        var gainSubclassFeatureCount = features
            .EnumerateArray()
            .Count(feature => feature.ValueKind == JsonValueKind.Object
                && feature.TryGetProperty("gainSubclassFeature", out var gainSubclassFeature)
                && gainSubclassFeature.ValueKind == JsonValueKind.True);

        if (gainSubclassFeatureCount == 0)
        {
            context.AddCase(new UnhandledCase(category, name, source, path, "no-subclass-grant-levels", "candidate", null, "No gainSubclassFeature markers found. Subclass feature level mapping may be impossible.", null, "SubclassFeatureLevelMapper"));
        }
    }

    private static void ScanElement(DataQualityContext context, string category, string name, string source, string path, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AnalyzeObject(context, category, name, source, path, element);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ScanElement(context, category, name, source, $"{path}[{index}]", item);
                    index++;
                }
                break;
            case JsonValueKind.String:
                AnalyzeText(context, category, name, source, path, element.GetString() ?? "");
                break;
        }
    }

    private static void AnalyzeObject(DataQualityContext context, string category, string name, string source, string path, JsonElement element)
    {
        if (IsEntryLikePath(path) && element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            var type = typeElement.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(type) && !KnownEntryTypes.Contains(type))
            {
                context.AddCase(new UnhandledCase(category, name, source, path, "unknown-entry-type", "unhandled", null, $"Unknown entry type '{type}'.", null, "EntryTypeParser"));
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            ScanElement(context, category, name, source, $"{path}.{property.Name}", property.Value);
        }
    }

    private static void AnalyzeText(DataQualityContext context, string category, string name, string source, string path, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsRuleTextPath(path))
        {
            return;
        }

        var emittedSemanticKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var detector in TextDetectors)
        {
            if (!detector.Pattern.IsMatch(text))
            {
                continue;
            }

            if (detector.CaseType == "ability-score-candidate" && AbilityRuleParser.TryBuildAbilityJsonFromText(text, out _))
            {
                continue;
            }

            var caseInfo = detector.ToCaseInfo();
            if (detector.CaseType == "choice-candidate")
            {
                var choiceCase = ClassifyChoiceText(text);
                if (choiceCase is null)
                {
                    continue;
                }

                caseInfo = choiceCase;
            }
            else if (detector.CaseType == "proficiency-candidate")
            {
                var proficiencyCase = ClassifyProficiencyText(text);
                if (proficiencyCase is null)
                {
                    continue;
                }

                caseInfo = proficiencyCase;
            }
            else if (detector.CaseType == "defense-candidate")
            {
                var defenseCase = ClassifyDefenseText(text);
                if (defenseCase is null)
                {
                    continue;
                }

                caseInfo = defenseCase;
            }
            else if (detector.CaseType == "spell-rule-candidate")
            {
                var spellCase = ClassifySpellText(category, text);
                if (spellCase is null)
                {
                    continue;
                }

                caseInfo = spellCase;
            }

            if (!emittedSemanticKeys.Add(GetSemanticKey(caseInfo.CaseType)))
            {
                continue;
            }

            context.AddCase(new UnhandledCase(
                category,
                name,
                source,
                path,
                caseInfo.CaseType,
                caseInfo.Severity,
                caseInfo.Confidence,
                caseInfo.Reason,
                Preview(text),
                caseInfo.SuggestedParser));
        }
    }

    private static string BuildDataQualityMarkdown(DataQualityContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Data Quality Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Source data: `{context.SourceDataPath}`");
        builder.AppendLine();
        builder.AppendLine("## Import Summary");
        builder.AppendLine();
        foreach (var pair in context.EntityCounts.OrderBy(pair => pair.Key))
        {
            builder.AppendLine($"- {pair.Key}: {pair.Value}");
        }

        builder.AppendLine();
        builder.AppendLine("## Case Summary");
        builder.AppendLine();
        AppendGroupedCounts(builder, "Severity", context.Cases.GroupBy(item => item.Severity));
        AppendGroupedCounts(builder, "Case Type", context.Cases.GroupBy(item => item.CaseType));
        AppendGroupedCounts(builder, "Category", context.Cases.GroupBy(item => item.Category));
        AppendParserBacklog(builder, context.Cases);
        AppendCaseTypeDetails(builder, context.Cases);
        builder.AppendLine();
        builder.AppendLine("## Highest Priority Cases");
        builder.AppendLine();
        foreach (var item in SortCasesForReview(context.Cases).Take(100))
        {
            AppendCase(builder, item);
        }

        if (context.Cases.Count > 100)
        {
            builder.AppendLine();
            builder.AppendLine($"_Showing first 100 of {context.Cases.Count} cases. See `unhandled-cases.json` for all cases._");
        }

        return builder.ToString();
    }

    private static string BuildUnhandledMarkdown(IReadOnlyList<UnhandledCase> cases)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Unhandled Cases");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        foreach (var group in cases.GroupBy(item => item.CaseType).OrderByDescending(group => CalculateParserPriority(group)).ThenBy(group => group.Key))
        {
            builder.AppendLine($"## {group.Key} ({group.Count()})");
            builder.AppendLine();
            AppendGroupedCounts(builder, "Categories", group.GroupBy(item => item.Category));
            foreach (var item in SortCasesForReview(group).Take(100))
            {
                AppendCase(builder, item);
            }

            if (group.Count() > 100)
            {
                builder.AppendLine($"_Showing first 100 of {group.Count()} cases in this group._");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void AppendParserBacklog(StringBuilder builder, IReadOnlyList<UnhandledCase> cases)
    {
        builder.AppendLine("## Suggested Parser Backlog");
        builder.AppendLine();
        builder.AppendLine("This is a coarse priority view. High-priority small groups are often good first parser targets; very large groups may need tighter detectors before implementation.");
        builder.AppendLine();
        builder.AppendLine("| Priority | Suggested parser | Cases | Main case types | Main categories |");
        builder.AppendLine("| ---: | --- | ---: | --- | --- |");

        foreach (var group in cases
            .Where(item => !string.IsNullOrWhiteSpace(item.SuggestedParser))
            .GroupBy(item => item.SuggestedParser!)
            .OrderByDescending(CalculateParserPriority)
            .ThenBy(group => group.Key))
        {
            var caseTypes = string.Join(", ", group
                .GroupBy(item => item.CaseType)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key)
                .Take(3)
                .Select(item => $"{item.Key} ({item.Count()})"));
            var categories = string.Join(", ", group
                .GroupBy(item => item.Category)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key)
                .Take(4)
                .Select(item => $"{item.Key} ({item.Count()})"));

            builder.AppendLine($"| {CalculateParserPriority(group):0.##} | `{group.Key}` | {group.Count()} | {caseTypes} | {categories} |");
        }

        builder.AppendLine();
    }

    private static void AppendCaseTypeDetails(StringBuilder builder, IReadOnlyList<UnhandledCase> cases)
    {
        builder.AppendLine("## Case Type Details");
        builder.AppendLine();
        foreach (var group in cases
            .GroupBy(item => item.CaseType)
            .OrderByDescending(group => CalculateParserPriority(group))
            .ThenBy(group => group.Key))
        {
            builder.AppendLine($"### {group.Key} ({group.Count()})");
            builder.AppendLine();
            builder.AppendLine("Top categories:");
            foreach (var category in group
                .GroupBy(item => item.Category)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key)
                .Take(6))
            {
                builder.AppendLine($"- {category.Key}: {category.Count()}");
            }

            builder.AppendLine();
            builder.AppendLine("Representative examples:");
            foreach (var item in PickRepresentativeCases(group, 5))
            {
                builder.AppendLine($"- `{item.Category}` {Fallback(item.Name, "(unnamed)")} ({Fallback(item.Source, "no source")}): `{item.Path}`");
            }

            builder.AppendLine();
        }
    }

    private static void AppendGroupedCounts(StringBuilder builder, string title, IEnumerable<IGrouping<string, UnhandledCase>> groups)
    {
        builder.AppendLine($"### {title}");
        builder.AppendLine();
        foreach (var group in groups.OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
        {
            builder.AppendLine($"- {group.Key}: {group.Count()}");
        }
        builder.AppendLine();
    }

    private static void AppendCase(StringBuilder builder, UnhandledCase item)
    {
        builder.AppendLine($"### {item.Category}: {Fallback(item.Name, "(unnamed)")} ({Fallback(item.Source, "no source")})");
        builder.AppendLine();
        builder.AppendLine($"- Severity: `{item.Severity}`");
        builder.AppendLine($"- Type: `{item.CaseType}`");
        builder.AppendLine($"- Path: `{item.Path}`");
        builder.AppendLine($"- Reason: {item.Reason}");
        if (item.Confidence is not null)
        {
            builder.AppendLine($"- Confidence: {item.Confidence:0.##}");
        }
        if (!string.IsNullOrWhiteSpace(item.SuggestedParser))
        {
            builder.AppendLine($"- Suggested parser: `{item.SuggestedParser}`");
        }
        if (!string.IsNullOrWhiteSpace(item.TextPreview))
        {
            builder.AppendLine();
            builder.AppendLine("> " + item.TextPreview.ReplaceLineEndings(" "));
        }
        builder.AppendLine();
    }

    private static IEnumerable<UnhandledCase> PickRepresentativeCases(IEnumerable<UnhandledCase> cases, int count)
    {
        return SortCasesForReview(cases)
            .GroupBy(item => $"{item.Category}|{item.Name}|{item.Source}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(count);
    }

    private static IEnumerable<UnhandledCase> SortCasesForReview(IEnumerable<UnhandledCase> cases)
    {
        return cases
            .OrderBy(item => SeverityRank(item.Severity))
            .ThenBy(item => CaseTypeRank(item.CaseType))
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Source)
            .ThenBy(item => item.Path);
    }

    private static double CalculateParserPriority(IEnumerable<UnhandledCase> cases)
    {
        var items = cases.ToList();
        var severityScore = items.Max(item => item.Severity switch
        {
            "error" => 12,
            "warning" => 5,
            "unhandled" => 4,
            "candidate" => 2,
            _ => 1
        });
        var caseTypeScore = items.Max(item => item.CaseType switch
        {
            "missing-name" => 95,
            "missing-parent-link" => 90,
            "repeatable-feat" => 88,
            "ability-score-candidate" => 84,
            "missing-class-features" => 82,
            "spell-choice-candidate" => 81,
            "ancestry-option-choice-candidate" => 81,
            "feature-option-choice-candidate" => 81,
            "ability-choice-candidate" => 81,
            "choice-candidate" => 80,
            "damage-type-choice-candidate" => 79,
            "target-choice-candidate" => 67,
            "damage-condition-defense-candidate" => 67,
            "defense-choice-candidate" => 67,
            "reactive-defense-candidate" => 67,
            "item-attuned-defense-candidate" => 67,
            "temporary-defense-candidate" => 67,
            "conditional-defense-candidate" => 67,
            "permanent-damage-resistance-candidate" => 66,
            "trap-damage-resistance-candidate" => 66,
            "mixed-defense-candidate" => 66,
            "object-defense-candidate" => 66,
            "damage-resistance-candidate" => 65,
            "defense-reference-candidate" => 65,
            "defense-bypass-candidate" => 65,
            "damage-immunity-candidate" => 64,
            "condition-save-effect-candidate" => 64,
            "effect-immunity-candidate" => 63,
            "condition-immunity-candidate" => 63,
            "damage-vulnerability-candidate" => 63,
            "background-spell-list-candidate" => 62,
            "innate-spell-grant-candidate" => 61,
            "level-gated-spell-grant-candidate" => 61,
            "spellcasting-ability-candidate" => 60,
            "spell-list-access-candidate" => 60,
            "spellcasting-prerequisite-candidate" => 59,
            "spell-reference-list-candidate" => 58,
            "item-spell-list-candidate" => 58,
            "spell-modifier-candidate" => 58,
            "item-spellcasting-bonus-candidate" => 58,
            "item-charge-spell-activation-candidate" => 58,
            "item-action-spell-activation-candidate" => 58,
            "item-passive-spell-access-candidate" => 58,
            "item-ritual-spell-activation-candidate" => 58,
            "item-temporary-spell-grant-candidate" => 58,
            "free-cast-spell-grant-candidate" => 58,
            "spell-slot-expenditure-effect-candidate" => 58,
            "spell-combat-interaction-candidate" => 58,
            "spell-removal-reference-candidate" => 46,
            "spellcasting-focus-candidate" => 57,
            "spell-slot-rule-candidate" => 57,
            "spell-slot-table-candidate" => 57,
            "spellbook-candidate" => 57,
            "item-spell-activation-candidate" => 56,
            "spell-affected-object-reference" => 45,
            "proficiency-uses-scaling-candidate" => 55,
            "proficiency-limited-damage-candidate" => 55,
            "proficiency-limited-save-effect-candidate" => 55,
            "proficiency-dc-scaling-candidate" => 54,
            "proficiency-damage-scaling-candidate" => 54,
            "proficiency-healing-scaling-candidate" => 54,
            "proficiency-movement-scaling-candidate" => 53,
            "proficiency-roll-scaling-candidate" => 53,
            "spell-effect-reference-candidate" => 44,
            "mixed-proficiency-candidate" => 76,
            "expertise-candidate" => 75,
            "all-skill-proficiency-candidate" => 75,
            "proficiency-choice-candidate" => 75,
            "skill-proficiency-candidate" => 74,
            "tool-proficiency-candidate" => 73,
            "language-proficiency-candidate" => 72,
            "weapon-proficiency-candidate" => 72,
            "armor-proficiency-candidate" => 72,
            "saving-throw-proficiency-candidate" => 72,
            "proficiency-bonus-scaling-candidate" => 71,
            "proficiency-candidate" => 70,
            "defense-candidate" => 62,
            "no-subclass-grant-levels" => 58,
            "foundry-overlay-duplicate" => 50,
            "duplicate-source-version" => 48,
            "defense-flavor-reference" => 36,
            "expertise-flavor-reference" => 35,
            "spell-flavor-reference" => 34,
            "spell-rule-candidate" => 30,
            _ => 2
        });
        var countScore = Math.Log10(items.Count + 1) * 5;

        return Math.Round(caseTypeScore + severityScore + countScore, 2);
    }

    private static int SeverityRank(string severity)
    {
        return severity switch
        {
            "error" => 0,
            "warning" => 1,
            "unhandled" => 2,
            "candidate" => 3,
            "wiki-only" => 4,
            _ => 5
        };
    }

    private static int CaseTypeRank(string caseType)
    {
        return caseType switch
        {
            "missing-name" => 0,
            "missing-parent-link" => 1,
            "missing-class-features" => 2,
            "repeatable-feat" => 3,
            "ability-score-candidate" => 4,
            "spell-choice-candidate" => 5,
            "ancestry-option-choice-candidate" => 6,
            "feature-option-choice-candidate" => 7,
            "ability-choice-candidate" => 8,
            "choice-candidate" => 9,
            "damage-type-choice-candidate" => 10,
            "mixed-proficiency-candidate" => 11,
            "expertise-candidate" => 12,
            "all-skill-proficiency-candidate" => 13,
            "proficiency-choice-candidate" => 14,
            "skill-proficiency-candidate" => 15,
            "tool-proficiency-candidate" => 16,
            "language-proficiency-candidate" => 17,
            "weapon-proficiency-candidate" => 18,
            "armor-proficiency-candidate" => 19,
            "saving-throw-proficiency-candidate" => 20,
            "proficiency-bonus-scaling-candidate" => 21,
            "proficiency-candidate" => 22,
            "target-choice-candidate" => 23,
            "damage-condition-defense-candidate" => 24,
            "defense-choice-candidate" => 25,
            "reactive-defense-candidate" => 26,
            "item-attuned-defense-candidate" => 27,
            "temporary-defense-candidate" => 28,
            "conditional-defense-candidate" => 29,
            "permanent-damage-resistance-candidate" => 30,
            "trap-damage-resistance-candidate" => 31,
            "mixed-defense-candidate" => 32,
            "object-defense-candidate" => 33,
            "damage-resistance-candidate" => 34,
            "defense-reference-candidate" => 35,
            "defense-bypass-candidate" => 36,
            "damage-immunity-candidate" => 37,
            "condition-save-effect-candidate" => 38,
            "effect-immunity-candidate" => 39,
            "condition-immunity-candidate" => 40,
            "damage-vulnerability-candidate" => 41,
            "defense-candidate" => 42,
            "level-gated-spell-grant-candidate" => 43,
            "innate-spell-grant-candidate" => 44,
            "spellcasting-ability-candidate" => 45,
            "spell-list-access-candidate" => 46,
            "spellcasting-prerequisite-candidate" => 47,
            "background-spell-list-candidate" => 48,
            "spell-reference-list-candidate" => 49,
            "item-spell-list-candidate" => 50,
            "spell-modifier-candidate" => 51,
            "item-spellcasting-bonus-candidate" => 52,
            "item-charge-spell-activation-candidate" => 53,
            "item-action-spell-activation-candidate" => 54,
            "item-passive-spell-access-candidate" => 55,
            "item-ritual-spell-activation-candidate" => 56,
            "item-temporary-spell-grant-candidate" => 57,
            "free-cast-spell-grant-candidate" => 58,
            "spell-slot-expenditure-effect-candidate" => 59,
            "spell-combat-interaction-candidate" => 60,
            "spellcasting-focus-candidate" => 61,
            "spell-slot-table-candidate" => 62,
            "spell-slot-rule-candidate" => 63,
            "spellbook-candidate" => 64,
            "item-spell-activation-candidate" => 65,
            "proficiency-limited-damage-candidate" => 66,
            "proficiency-limited-save-effect-candidate" => 67,
            "proficiency-uses-scaling-candidate" => 68,
            "proficiency-dc-scaling-candidate" => 69,
            "proficiency-damage-scaling-candidate" => 70,
            "proficiency-healing-scaling-candidate" => 71,
            "proficiency-movement-scaling-candidate" => 72,
            "proficiency-roll-scaling-candidate" => 73,
            "spell-removal-reference-candidate" => 74,
            "spell-affected-object-reference" => 75,
            "spell-effect-reference-candidate" => 76,
            "spell-rule-candidate" => 77,
            "no-subclass-grant-levels" => 78,
            "foundry-overlay-duplicate" => 79,
            "duplicate-source-version" => 80,
            "defense-flavor-reference" => 81,
            "expertise-flavor-reference" => 82,
            "spell-flavor-reference" => 83,
            _ => 60
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static bool IsUnnamedRaceOverlay(string category, string source, JsonElement element)
    {
        return category is "race-version"
            && !string.IsNullOrWhiteSpace(ReadString(element, "raceName"))
            && !string.IsNullOrWhiteSpace(ReadString(element, "raceSource"))
            && string.Equals(source, ReadString(element, "raceSource"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntryLikePath(string path)
    {
        return path.Contains(".entries", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".items", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".rows", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".classTableGroups", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".subclassTableGroups", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuleTextPath(string path)
    {
        if (!IsEntryLikePath(path))
        {
            return false;
        }

        return !path.EndsWith(".name", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".source", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".type", StringComparison.OrdinalIgnoreCase);
    }

    private static string Preview(string text)
    {
        var normalized = WhitespaceRegex().Replace(text, " ").Trim();
        return normalized.Length <= PreviewLength
            ? normalized
            : normalized[..PreviewLength] + "...";
    }

    private static string Fallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static TextCaseInfo? ClassifyChoiceText(string text)
    {
        var cleaned = text.Trim();
        if (FlavorChoiceRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (NonBuildChoiceRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (EquipmentChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "equipment-choice-candidate",
                "candidate",
                0.82,
                "Text appears to describe a starting equipment choice.",
                "StartingEquipmentChoiceParser");
        }

        if (SpellChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-choice-candidate",
                "candidate",
                0.86,
                "Text appears to require choosing one or more spells or cantrips.",
                "SpellChoiceTextParser");
        }

        if (AbilityChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "ability-choice-candidate",
                "candidate",
                0.84,
                "Text appears to require choosing an ability score.",
                "AbilityChoiceTextParser");
        }

        if (AncestryOptionChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "ancestry-option-choice-candidate",
                "candidate",
                0.84,
                "Text appears to require choosing an ancestry, lineage, legacy, or similar character option.",
                "AncestryOptionChoiceParser");
        }

        if (DamageTypeChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "damage-type-choice-candidate",
                "candidate",
                0.84,
                "Text appears to require choosing a damage type or energy affinity.",
                "DamageTypeChoiceParser");
        }

        if (FeatureOptionChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "feature-option-choice-candidate",
                "candidate",
                0.8,
                "Text appears to require choosing between named feature options or effects.",
                "FeatureOptionChoiceParser");
        }

        if (TargetChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "target-choice-candidate",
                "candidate",
                0.72,
                "Text appears to describe choosing a runtime target rather than a character build option.",
                "TargetChoiceTextParser");
        }

        if (ToolChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "tool-choice-candidate",
                "candidate",
                0.85,
                "Text appears to require choosing a tool or tool type.",
                "ToolChoiceTextParser");
        }

        if (LanguageChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "language-choice-candidate",
                "candidate",
                0.85,
                "Text appears to require choosing one or more languages.",
                "LanguageChoiceTextParser");
        }

        if (SkillChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "skill-choice-candidate",
                "candidate",
                0.85,
                "Text appears to require choosing one or more skills.",
                "SkillChoiceTextParser");
        }

        return new TextCaseInfo(
            "choice-candidate",
            "candidate",
            0.75,
            "Text appears to require a user choice.",
            "ChoiceTextParser");
    }

    private static TextCaseInfo? ClassifyProficiencyText(string text)
    {
        var cleaned = text.Trim();
        if (ProficiencyHeadingRegex().IsMatch(cleaned))
        {
            return null;
        }

        var hasExpertise = ExpertiseRegex().IsMatch(cleaned);
        var hasSkill = SkillProficiencyRegex().IsMatch(cleaned);
        var hasTool = ToolProficiencyRegex().IsMatch(cleaned);
        var hasLanguage = LanguageProficiencyRegex().IsMatch(cleaned);
        var hasWeapon = WeaponProficiencyRegex().IsMatch(cleaned);
        var hasArmor = ArmorProficiencyRegex().IsMatch(cleaned);
        var hasSavingThrow = SavingThrowProficiencyRegex().IsMatch(cleaned);
        var categoryCount = CountTrue(hasSkill, hasTool, hasLanguage, hasWeapon, hasArmor, hasSavingThrow);

        if (ExpertiseFlavorRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "expertise-flavor-reference",
                "wiki-only",
                0.72,
                "Text uses expertise as descriptive flavor rather than granting expertise.",
                "NoMechanicalRuleParser");
        }

        if (hasExpertise)
        {
            return new TextCaseInfo(
                "expertise-candidate",
                "candidate",
                0.86,
                "Text appears to grant expertise or double a proficiency bonus.",
                "ExpertiseTextParser");
        }

        if (ProficiencyBonusScalingRegex().IsMatch(cleaned))
        {
            return ClassifyProficiencyBonusScalingText(cleaned);
        }

        if (AllSkillProficiencyRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "all-skill-proficiency-candidate",
                "candidate",
                0.86,
                "Text appears to grant proficiency in all skills.",
                "AllSkillProficiencyParser");
        }

        if (ProficiencyChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "proficiency-choice-candidate",
                "candidate",
                0.84,
                "Text appears to grant a choice between one or more proficiency types.",
                "ProficiencyChoiceParser");
        }

        if (categoryCount > 1 || MixedProficiencyRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "mixed-proficiency-candidate",
                "candidate",
                0.78,
                "Text appears to grant or modify multiple proficiency types.",
                "ProficiencyTextParser");
        }

        if (hasSkill)
        {
            return new TextCaseInfo(
                "skill-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify skill proficiency.",
                "SkillProficiencyTextParser");
        }

        if (hasTool)
        {
            return new TextCaseInfo(
                "tool-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify tool proficiency.",
                "ToolProficiencyTextParser");
        }

        if (hasLanguage)
        {
            return new TextCaseInfo(
                "language-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify language proficiency.",
                "LanguageProficiencyTextParser");
        }

        if (hasWeapon)
        {
            return new TextCaseInfo(
                "weapon-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify weapon proficiency.",
                "WeaponProficiencyTextParser");
        }

        if (hasArmor)
        {
            return new TextCaseInfo(
                "armor-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify armor or shield proficiency.",
                "ArmorProficiencyTextParser");
        }

        if (hasSavingThrow)
        {
            return new TextCaseInfo(
                "saving-throw-proficiency-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify saving throw proficiency.",
                "SavingThrowProficiencyTextParser");
        }

        return new TextCaseInfo(
            "proficiency-candidate",
            "candidate",
            0.65,
            "Text references proficiency, but the target type is not clear enough to classify.",
            "ProficiencyTextParser");
    }

    private static TextCaseInfo? ClassifyDefenseText(string text)
    {
        var cleaned = text.Trim();
        if (DefenseHeadingRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (DefenseFlavorRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "defense-flavor-reference",
                "wiki-only",
                0.68,
                "Text uses resistance, immunity, or vulnerability as descriptive language rather than a mechanical defense.",
                "NoMechanicalRuleParser");
        }

        if (DefenseReferenceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "defense-reference-candidate",
                "candidate",
                0.82,
                "Text appears to reference a shared resistance or defensive item entry.",
                "DefenseReferenceParser");
        }

        if (ObjectDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "object-defense-candidate",
                "candidate",
                0.82,
                "Text appears to define AC, HP, immunity, or resistance for an object or summoned object.",
                "ObjectDefenseTextParser");
        }

        if (DefenseBypassRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "defense-bypass-candidate",
                "candidate",
                0.8,
                "Text appears to bypass or ignore resistance or immunity rather than grant it.",
                "DefenseBypassTextParser");
        }

        var hasResistance = DamageResistanceRegex().IsMatch(cleaned);
        var hasDamageImmunity = DamageImmunityRegex().IsMatch(cleaned);
        var hasConditionImmunity = ConditionImmunityRegex().IsMatch(cleaned);
        var hasVulnerability = DamageVulnerabilityRegex().IsMatch(cleaned);
        var hasDefenseChoice = DefenseChoiceRegex().IsMatch(cleaned);
        var hasEffectImmunity = EffectImmunityRegex().IsMatch(cleaned);
        var hasConditionSaveEffect = ConditionSaveEffectRegex().IsMatch(cleaned);
        var categoryCount = CountTrue(hasResistance, hasDamageImmunity, hasConditionImmunity, hasVulnerability, hasDefenseChoice, hasEffectImmunity, hasConditionSaveEffect);

        if (DamageConditionDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "damage-condition-defense-candidate",
                "candidate",
                0.82,
                "Text appears to combine damage immunity/resistance with condition immunity.",
                "DamageConditionDefenseParser");
        }

        if (hasDefenseChoice)
        {
            return new TextCaseInfo(
                "defense-choice-candidate",
                "candidate",
                0.84,
                "Text appears to choose or derive a defensive trait from a selected damage type, ancestry, table, or option.",
                "DefenseChoiceTextParser");
        }

        if (categoryCount > 0 && ReactiveDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "reactive-defense-candidate",
                "candidate",
                0.84,
                "Text appears to grant resistance or another defense as a reaction or response to damage.",
                "ReactiveDefenseParser");
        }

        if (categoryCount > 0 && ItemAttunedDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-attuned-defense-candidate",
                "candidate",
                0.84,
                "Text appears to grant a defensive trait while wearing, holding, carrying, or attuned to an item.",
                "ItemAttunedDefenseParser");
        }

        if (categoryCount > 0 && TemporaryDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "temporary-defense-candidate",
                "candidate",
                0.82,
                "Text appears to grant a defensive trait for a limited duration or until a rest/event.",
                "TemporaryDefenseParser");
        }

        if (categoryCount > 0 && ConditionalDefenseRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "conditional-defense-candidate",
                "candidate",
                0.82,
                "Text appears to grant a defensive trait only while a condition, state, or circumstance is true.",
                "ConditionalDefenseParser");
        }

        if (categoryCount > 1)
        {
            return new TextCaseInfo(
                "mixed-defense-candidate",
                "candidate",
                0.78,
                "Text appears to grant or modify multiple defensive traits.",
                "DefenseTextParser");
        }

        if (TrapDamageResistanceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "trap-damage-resistance-candidate",
                "candidate",
                0.84,
                "Text appears to grant resistance to trap damage.",
                "TrapDamageResistanceParser");
        }

        if (PermanentDamageResistanceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "permanent-damage-resistance-candidate",
                "candidate",
                0.86,
                "Text appears to grant an unconditional damage resistance.",
                "PermanentDamageResistanceParser");
        }

        if (hasResistance)
        {
            return new TextCaseInfo(
                "damage-resistance-candidate",
                "candidate",
                0.84,
                "Text appears to grant or modify damage resistance.",
                "DamageResistanceTextParser");
        }

        if (hasDamageImmunity)
        {
            return new TextCaseInfo(
                "damage-immunity-candidate",
                "candidate",
                0.84,
                "Text appears to grant or modify damage immunity.",
                "DamageImmunityTextParser");
        }

        if (hasConditionSaveEffect)
        {
            return new TextCaseInfo(
                "condition-save-effect-candidate",
                "candidate",
                0.78,
                "Text appears to impose or move a condition through a save or effect rather than grant a defensive trait.",
                "ConditionSaveEffectParser");
        }

        if (hasEffectImmunity)
        {
            return new TextCaseInfo(
                "effect-immunity-candidate",
                "candidate",
                0.78,
                "Text appears to grant immunity to an effect, spell, curse, or environment rather than a damage type.",
                "EffectImmunityTextParser");
        }

        if (hasConditionImmunity)
        {
            return new TextCaseInfo(
                "condition-immunity-candidate",
                "candidate",
                0.82,
                "Text appears to grant or modify condition or disease immunity.",
                "ConditionImmunityTextParser");
        }

        if (hasVulnerability)
        {
            return new TextCaseInfo(
                "damage-vulnerability-candidate",
                "candidate",
                0.84,
                "Text appears to grant or modify damage vulnerability.",
                "DamageVulnerabilityTextParser");
        }

        return new TextCaseInfo(
            "defense-candidate",
            "candidate",
            0.64,
            "Text references resistance, immunity, or vulnerability, but the defensive trait is not clear enough to classify.",
            "DefenseTextParser");
    }

    private static TextCaseInfo ClassifyProficiencyBonusScalingText(string text)
    {
        if (ProficiencyLimitedDamageRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-limited-damage-candidate",
                "candidate",
                0.84,
                "Text appears to grant a damage effect whose uses or damage scale by proficiency bonus.",
                "ProficiencyLimitedDamageParser");
        }

        if (ProficiencyLimitedSaveEffectRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-limited-save-effect-candidate",
                "candidate",
                0.83,
                "Text appears to grant a save-based effect whose uses scale by proficiency bonus.",
                "ProficiencyLimitedSaveEffectParser");
        }

        if (ProficiencyUsesScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-uses-scaling-candidate",
                "candidate",
                0.84,
                "Text appears to scale trait uses by proficiency bonus.",
                "ProficiencyUsesScalingParser");
        }

        if (ProficiencyDcScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-dc-scaling-candidate",
                "candidate",
                0.82,
                "Text appears to define a save DC or escape DC using proficiency bonus.",
                "ProficiencyDcScalingParser");
        }

        if (ProficiencyDamageScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-damage-scaling-candidate",
                "candidate",
                0.82,
                "Text appears to scale damage by proficiency bonus.",
                "ProficiencyDamageScalingParser");
        }

        if (ProficiencyHealingScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-healing-scaling-candidate",
                "candidate",
                0.82,
                "Text appears to scale healing or temporary hit points by proficiency bonus.",
                "ProficiencyHealingScalingParser");
        }

        if (ProficiencyMovementScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-movement-scaling-candidate",
                "candidate",
                0.8,
                "Text appears to scale movement or distance by proficiency bonus.",
                "ProficiencyMovementScalingParser");
        }

        if (ProficiencyRollScalingRegex().IsMatch(text))
        {
            return new TextCaseInfo(
                "proficiency-roll-scaling-candidate",
                "candidate",
                0.78,
                "Text appears to add proficiency bonus to a roll or check.",
                "ProficiencyRollScalingParser");
        }

        return new TextCaseInfo(
            "proficiency-bonus-scaling-candidate",
            "candidate",
            0.8,
            "Text appears to scale uses, rolls, or effects by proficiency bonus.",
            "ProficiencyBonusScalingParser");
    }

    private static TextCaseInfo? ClassifySpellText(string category, string text)
    {
        var cleaned = text.Trim();
        if (SpellHeadingRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (SpellFlavorRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (SpellFlavorReferenceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-flavor-reference",
                "wiki-only",
                0.68,
                "Text references spells as descriptive flavor rather than a mechanical spell grant.",
                "NoMechanicalRuleParser");
        }

        if (SpellLevelHeadingRegex().IsMatch(cleaned))
        {
            return null;
        }

        if (SpellReferenceListRegex().IsMatch(cleaned))
        {
            if (string.Equals(category, "background", StringComparison.OrdinalIgnoreCase))
            {
                return new TextCaseInfo(
                    "background-spell-list-candidate",
                    "candidate",
                    0.86,
                    "Text appears to be a background spell-list table entry.",
                    "BackgroundSpellListParser");
            }

            if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "item-group", StringComparison.OrdinalIgnoreCase))
            {
                return new TextCaseInfo(
                    "item-spell-list-candidate",
                    "candidate",
                    0.8,
                    "Text appears to be an item spell-list table or embedded item spell reference.",
                    "ItemSpellListParser");
            }

            return new TextCaseInfo(
                "spell-reference-list-candidate",
                "candidate",
                0.82,
                "Text appears to be a table or list entry that references one or more spells.",
                "SpellReferenceListParser");
        }

        if (SpellcastingPrerequisiteRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spellcasting-prerequisite-candidate",
                "candidate",
                0.82,
                "Text appears to declare a Spellcasting or Pact Magic prerequisite.",
                "SpellcastingPrerequisiteParser");
        }

        if (SpellListAccessRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-list-access-candidate",
                "candidate",
                0.84,
                "Text appears to add spells to a class spell list.",
                "SpellListAccessParser");
        }

        if (SpellcastingFocusRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spellcasting-focus-candidate",
                "candidate",
                0.82,
                "Text appears to define a spellcasting focus or similar casting implement.",
                "SpellcastingFocusParser");
        }

        if (FreeCastSpellGrantRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "free-cast-spell-grant-candidate",
                "candidate",
                0.86,
                "Text appears to grant a spell that can be cast once without spending a spell slot.",
                "FreeCastSpellGrantParser");
        }

        if (SpellSlotExpenditureEffectRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-slot-expenditure-effect-candidate",
                "candidate",
                0.82,
                "Text appears to spend a spell slot for a feature effect rather than grant spellcasting.",
                "SpellSlotExpenditureEffectParser");
        }

        if (SpellSlotRuleRegex().IsMatch(cleaned))
        {
            if (SpellSlotTableRegex().IsMatch(cleaned))
            {
                return new TextCaseInfo(
                    "spell-slot-table-candidate",
                    "candidate",
                    0.84,
                    "Text appears to be a spell-slot table heading or table label.",
                    "SpellSlotTableParser");
            }

            return new TextCaseInfo(
                "spell-slot-rule-candidate",
                "candidate",
                0.8,
                "Text appears to grant, restore, spend, or scale spell slots.",
                "SpellSlotRuleParser");
        }

        if (SpellbookRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spellbook-candidate",
                "candidate",
                0.78,
                "Text appears to describe a spellbook, ritual book, or recorded spell rules.",
                "SpellbookTextParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemSpellcastingBonusRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-spellcasting-bonus-candidate",
                "candidate",
                0.84,
                "Text appears to grant a bonus to spell attacks, spell save DCs, or spellcasting checks from an item.",
                "ItemSpellcastingBonusParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemTemporarySpellGrantRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-temporary-spell-grant-candidate",
                "candidate",
                0.84,
                "Text appears to let an item temporarily grant or replace a known, prepared, or castable spell.",
                "ItemTemporarySpellGrantParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemChargeSpellActivationRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-charge-spell-activation-candidate",
                "candidate",
                0.84,
                "Text appears to cast spells from an item by expending charges.",
                "ItemChargeSpellActivationParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemActionSpellActivationRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-action-spell-activation-candidate",
                "candidate",
                0.82,
                "Text appears to cast spells from an item using an action, bonus action, or Magic action.",
                "ItemActionSpellActivationParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemRitualSpellActivationRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-ritual-spell-activation-candidate",
                "candidate",
                0.82,
                "Text appears to cast a spell from an item as a ritual or ritual-like activation.",
                "ItemRitualSpellActivationParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemPassiveSpellAccessRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-passive-spell-access-candidate",
                "candidate",
                0.8,
                "Text appears to passively allow spellcasting from an item without a standard charge/action phrase.",
                "ItemPassiveSpellAccessParser");
        }

        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase)
            && ItemSpellActivationRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "item-spell-activation-candidate",
                "candidate",
                0.8,
                "Text appears to describe spellcasting through an item activation or charge effect.",
                "ItemSpellActivationParser");
        }

        if (LevelGatedSpellGrantRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "level-gated-spell-grant-candidate",
                "candidate",
                0.86,
                "Text appears to grant spells when a character reaches a specific level.",
                "LevelGatedSpellGrantParser");
        }

        if (SpellModifierRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-modifier-candidate",
                "candidate",
                0.78,
                "Text appears to modify spells, spell attacks, spell damage, components, or concentration.",
                "SpellModifierTextParser");
        }

        if (SpellChoiceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-choice-candidate",
                "candidate",
                0.86,
                "Text appears to require choosing one or more spells or cantrips.",
                "SpellChoiceTextParser");
        }

        if (SpellcastingAbilityRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spellcasting-ability-candidate",
                "candidate",
                0.86,
                "Text appears to define the spellcasting ability for granted spells.",
                "SpellcastingAbilityTextParser");
        }

        if (InnateSpellGrantRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "innate-spell-grant-candidate",
                "candidate",
                0.84,
                "Text appears to grant known, prepared, or castable spells outside normal class spellcasting.",
                "InnateSpellGrantParser");
        }

        if (SpellAffectedObjectReferenceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-affected-object-reference",
                "wiki-only",
                0.68,
                "Text references a creature or object affected by a spell rather than granting spellcasting.",
                "NoMechanicalRuleParser");
        }

        if (SpellRemovalReferenceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-removal-reference-candidate",
                "candidate",
                0.72,
                "Text references spells that can remove, end, or suppress another effect.",
                "SpellRemovalReferenceParser");
        }

        if (SpellCombatInteractionRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-combat-interaction-candidate",
                "candidate",
                0.72,
                "Text modifies or reacts to spell damage, spell saves, or spell-created conditions without granting spells.",
                "SpellCombatInteractionParser");
        }

        if (SpellEffectReferenceRegex().IsMatch(cleaned))
        {
            return new TextCaseInfo(
                "spell-effect-reference-candidate",
                "candidate",
                0.7,
                "Text references spells as part of an effect, trigger, or exception rather than granting a spell.",
                "SpellEffectReferenceParser");
        }

        return new TextCaseInfo(
            "spell-rule-candidate",
            "candidate",
            0.55,
            "Text appears to grant or modify spellcasting or spell access.",
            "SpellGrantParser");
    }

    private static string GetSemanticKey(string caseType)
    {
        return caseType switch
        {
            "skill-choice-candidate" or "skill-proficiency-candidate" => "skill-proficiency",
            "tool-choice-candidate" or "tool-proficiency-candidate" => "tool-proficiency",
            "language-choice-candidate" or "language-proficiency-candidate" => "language-proficiency",
            "spell-choice-candidate" => "spell-choice",
            _ => caseType
        };
    }

    private static int CountTrue(params bool[] values)
    {
        return values.Count(value => value);
    }

    [GeneratedRegex(@"\b(choose|select|pick)\s+(one|two|three|a|an|any|from|\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChoiceTextRegex();

    [GeneratedRegex(@"\b(?:roll|choose from|pick\s+(?:a|an|your)?\s*(?:favorite|goal|ideal|bond|flaw|event|routine|scam|origin|homeland|territory|districts?|secret|quirk|personality|characteristic)|choose\s+(?:one\s+of\s+(?:the\s+)?(?:\w+\s+){0,3})?(?:a|an|your)?\s*(?:favorite|goal|ideal|bond|flaw|event|routine|scam|origin|homeland|territory|districts?|secret|quirk|personality|characteristic))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlavorChoiceRegex();

    [GeneratedRegex(@"\b(?:DM can choose|DM's choice|pick a lock|disarm a trap)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonBuildChoiceRegex();

    [GeneratedRegex(@"\bChoose A or B\b|\{@item\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EquipmentChoiceRegex();

    [GeneratedRegex(@"\b(?:choose|select)\b.*\b(?:cantrip|cantrips|spell|spells|spell list|class list)\b|\b(?:cantrip|cantrips|spell|spells)\s+of your choice\b|\bchoose.*\{@filter[^}]*\bspells\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:one\s+)?ability score\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:(?:a|an|one)\s+)?(?:kind of dragon|lineage|legacy|ancestry|animal enhancement|benefit|type of plane|moon|college|class)\b|\bchoose\s+one\s+of\s+(?:the\s+)?(?:following\s+)?(?:legacy|lineage|benefit|benefits|options)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AncestryOptionChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:one\s+of\s+the\s+following\s+)?(?:damage types?|energy type|acid|cold|fire|lightning|thunder|necrotic|radiant|force|psychic|poison)\b|\bpick a damage type\b|\bchoose a different damage type\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageTypeChoiceRegex();

    [GeneratedRegex(@"\b(?:choose|select)\s+(?:one|two|three|a|an|\d+)\s+(?:of\s+the\s+)?(?:following\s+)?(?:options?|effects?|features?|benefits?)\b|\bVariant Feature \(Choose 1\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeatureOptionChoiceRegex();

    [GeneratedRegex(@"\b(?:choose|select)\s+(?:one|a|an|\d+)\s+(?:ally|creature|target|space|item|card)\b|\byou can choose yourself\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:one|two|three|four|a|an|\d+).*(?:tool|tools|artisan's tools|gaming set|musical instrument|instrument|supplies|kit)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:one|two|three|four|a|an|\d+).*(?:language|languages|celestial|draconic|goblin|minotaur|common|elvish|giant|kraul|loxodon|sylvan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageChoiceRegex();

    [GeneratedRegex(@"\bchoose\s+(?:one|two|three|four|a|an|\d+).*(?:skill|skills)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillChoiceRegex();

    [GeneratedRegex(@"\b(proficiency|proficiencies|expertise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyTextRegex();

    [GeneratedRegex(@"^\s*proficiencies\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyHeadingRegex();

    [GeneratedRegex(@"\b(?:expertise|proficiency bonus is doubled|double(?:d)?\s+(?:your\s+)?proficiency bonus|twice your proficiency bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExpertiseRegex();

    [GeneratedRegex(@"\b(?:define your expertise|area of expertise|areas of expertise|with expertise in|expertise with|professional expertise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExpertiseFlavorRegex();

    [GeneratedRegex(@"\b(?:equal to your (?:{@variantrule\s+)?proficiency|proficiency bonus|number of times equal to your|PB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyBonusScalingRegex();

    [GeneratedRegex(@"\b(?:number of times equal to your|number of .* equal to your .*Proficiency Bonus|use this trait a number of times|use this .* a number of times|regain all expended uses|PB uses)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyUsesScalingRegex();

    [GeneratedRegex(@"\b(?:damage|extra damage|necrotic damage|force damage|psychic damage|radiant damage|fire damage|cold damage|lightning damage|thunder damage|acid damage|poison damage)\b.*\b(?:proficiency bonus|PB|number of times equal to your)\b|\b(?:proficiency bonus|PB|number of times equal to your)\b.*\b(?:damage|extra damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyLimitedDamageRegex();

    [GeneratedRegex(@"\b(?:saving throw|save DC|must make|failed save|on a failed save)\b.*\b(?:proficiency bonus|PB|number of times equal to your)\b|\b(?:proficiency bonus|PB|number of times equal to your)\b.*\b(?:saving throw|save DC|failed save|condition)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyLimitedSaveEffectRegex();

    [GeneratedRegex(@"\b(?:DC equals|DC for this|escape \{@dc|save DC|DC .*Proficiency Bonus|8 \+ your proficiency bonus|8 plus your proficiency bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyDcScalingRegex();

    [GeneratedRegex(@"\b(?:damage equal to|extra damage equals|extra damage .*Proficiency Bonus|add your .*Proficiency Bonus.*damage|add your proficiency bonus.*damage|takes .* plus your proficiency bonus|damage .* proficiency bonus|damage .*Proficiency Bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyDamageScalingRegex();

    [GeneratedRegex(@"\b(?:hit points equal to|temporary hit points equal to|Hit Points.*equal to.*Proficiency Bonus|regains .* plus your|healing .* proficiency bonus|restore .* plus your proficiency bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyHealingScalingRegex();

    [GeneratedRegex(@"\b(?:feet equal to|five times your proficiency bonus|five times your .*Proficiency Bonus|distance .* proficiency bonus|jump .* proficiency bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyMovementScalingRegex();

    [GeneratedRegex(@"\b(?:add your proficiency bonus to|add your .*Proficiency Bonus.* to|bonus to the roll equal to|roll .* plus your proficiency bonus|initiative rolls|Initiative.*Proficiency Bonus|ability check .* proficiency bonus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyRollScalingRegex();

    [GeneratedRegex(@"\b(?:skill|skills|{@skill\b|acrobatics|animal handling|arcana|athletics|deception|history|insight|intimidation|investigation|medicine|nature|perception|performance|persuasion|religion|sleight of hand|stealth|survival)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillProficiencyRegex();

    [GeneratedRegex(@"\bgain proficiency in all skills\b|\bproficiency in all skills\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AllSkillProficiencyRegex();

    [GeneratedRegex(@"\b(?:proficiency|proficiencies|proficient)\b.*\b(?:of your choice|your choice|choose|chosen)\b|\b(?:choose|chosen)\b.*\b(?:proficiency|proficiencies|proficient)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyChoiceRegex();

    [GeneratedRegex(@"\b(?:tool|tools|artisan's tools|gaming set|musical instrument|musical instruments|instrument|instruments|utensil|utensils|vehicle|vehicles|kit|supplies|thieves' tools|disguise kit|forgery kit|herbalism kit|navigator's tools|poisoner's kit)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolProficiencyRegex();

    [GeneratedRegex(@"\b(?:language|languages|common|dwarvish|elvish|giant|gnomish|goblin|halfling|orc|abyssal|celestial|draconic|deep speech|infernal|primordial|sylvan|undercommon|minotaur)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageProficiencyRegex();

    [GeneratedRegex(@"\b(?:weapon|weapons|firearm|firearms|battleaxe|handaxe|light hammer|warhammer|longsword|shortsword|shortbow|longbow|khopesh|spear|javelin|rapier|hand crossbow|trident|net|simple weapons|martial weapons)\b|\{@item\s+(?:battleaxe|handaxe|light hammer|warhammer|longsword|shortsword|shortbow|longbow|spear|javelin|rapier|hand crossbow|trident|net)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeaponProficiencyRegex();

    [GeneratedRegex(@"\b(?:armor|armour|shield|shields|light armor|medium armor|heavy armor)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArmorProficiencyRegex();

    [GeneratedRegex(@"\b(?:saving throw|saving throws|strength saving throws?|dexterity saving throws?|constitution saving throws?|intelligence saving throws?|wisdom saving throws?|charisma saving throws?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SavingThrowProficiencyRegex();

    [GeneratedRegex(@"\b(?:skill or tool|skill, tool|tools? or skills?|skills? or tools?|language or tool|tool or language)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MixedProficiencyRegex();

    [GeneratedRegex(@"\b(resistance|resistant|immunity|immune|vulnerability|vulnerable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseTextRegex();

    [GeneratedRegex(@"^\s*(?:damage|draconic)\s+resistance\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseHeadingRegex();

    [GeneratedRegex(@"\b(?:water-resistant|ordinary people|rigid discipline|leave their mark|resistance movement|resistance efforts|resistance army|resistance member|the resistance)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseFlavorRegex();

    [GeneratedRegex(@"^\s*(?:\{#itemEntry\s+(?:Potion|Ring) of Resistance(?:\|[^}]*)?}|Armor of (?:Acid|Cold|Fire|Force|Lightning|Necrotic|Poison|Psychic|Radiant|Thunder) Resistance(?:\|[^}]*)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseReferenceRegex();

    [GeneratedRegex(@"\b(?:object|painting|tower|door|walls?|roof|device|orb|copy|figurine|construct|has AC|AC \d+|hit points|HP \d+)\b.*\b(?:immunity|immune|resistance|resistant|vulnerability|vulnerable)\b|\b(?:immunity|immune|resistance|resistant)\b.*\b(?:object|painting|tower|door|walls?|roof|device|orb|copy|figurine|construct|hit points|HP \d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectDefenseRegex();

    [GeneratedRegex(@"\b(?:overcoming|overcome|ignores?|ignore|bypass(?:es)?)\b.*\b(?:resistance|immunity|resistant|immune|nonmagical attacks|nonmagical damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseBypassRegex();

    [GeneratedRegex(@"\b(?:resistance|resistant)\b.*\b(?:acid|bludgeoning|cold|fire|force|lightning|necrotic|piercing|poison|psychic|radiant|slashing|thunder|all damage|damage type|\{\{damageType\}\}|damage)\b|\b(?:acid|bludgeoning|cold|fire|force|lightning|necrotic|piercing|poison|psychic|radiant|slashing|thunder)\s+damage\b.*\bresistance\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageResistanceRegex();

    [GeneratedRegex(@"\b(?:immune|immunity)\b.*\b(?:acid|bludgeoning|cold|fire|force|lightning|necrotic|piercing|poison|psychic|radiant|slashing|thunder|damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageImmunityRegex();

    [GeneratedRegex(@"\b(?:choose|chosen|choice|your choice|DM's choice|determined by|associated with|from .* table|ancestry|lineage|legacy|\{\{damageType\}\})\b.*\b(?:resistance|resistant|immunity|immune|vulnerability|vulnerable|damage type)\b|\b(?:resistance|resistant|immunity|immune|vulnerability|vulnerable|damage type)\b.*\b(?:choose|chosen|choice|your choice|DM's choice|determined by|associated with|from .* table|ancestry|lineage|legacy|\{\{damageType\}\})\b|\b(?:choice affects|damage resistance.*determined)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseChoiceRegex();

    [GeneratedRegex(@"\b(?:immune|immunity)\b.*\b(?:curse|curses|spell|spells|effect|effects|magic|magical|read your thoughts|lying|telepathically|airless environment|gas|inhaled|disease)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EffectImmunityRegex();

    [GeneratedRegex(@"\b(?:must succeed on|make a .* saving throw|failed save|on a failed save|move one of the following conditions|be \{@condition|become \{@condition)\b.*\b(?:\{@condition|condition|poisoned|frightened|blinded|deafened|charmed|paralyzed|stunned)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionSaveEffectRegex();

    [GeneratedRegex(@"\b(?:immune|immunity|can't be|cannot be)\b.*\b(?:disease|exhaustion|poisoned|charmed|frightened|paralyzed|petrified|stunned|blinded|deafened|grappled|incapacitated|invisible|prone|restrained|unconscious|\{@condition\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionImmunityRegex();

    [GeneratedRegex(@"\b(?:vulnerability|vulnerable)\b.*\b(?:acid|bludgeoning|cold|fire|force|lightning|necrotic|piercing|poison|psychic|radiant|slashing|thunder|damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageVulnerabilityRegex();

    [GeneratedRegex(@"\b(?:poison|poisoned|fire|cold|lightning|necrotic|radiant|psychic|thunder|acid)\s+damage\b.*\b(?:condition|poisoned|charmed|frightened|paralyzed|petrified|stunned|blinded|deafened)\b|\b(?:condition|poisoned|charmed|frightened|paralyzed|petrified|stunned|blinded|deafened)\b.*\b(?:poison|fire|cold|lightning|necrotic|radiant|psychic|thunder|acid)\s+damage\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageConditionDefenseRegex();

    [GeneratedRegex(@"\b(?:reaction|immediately after|when you take|when .* hits you|takes? damage|that instance of damage|that attack's damage)\b.*\b(?:resistance|resistant|immune|immunity)\b|\b(?:resistance|resistant|immune|immunity)\b.*\b(?:reaction|that instance of damage|that attack's damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReactiveDefenseRegex();

    [GeneratedRegex(@"\b(?:while|when|as long as)\b.*\b(?:wearing|holding|carrying|attuned|attuned to|wielding|on your person)\b.*\b(?:resistance|resistant|immunity|immune|vulnerability|vulnerable|Damage Absorption)\b|\b(?:resistance|resistant|immunity|immune|Damage Absorption)\b.*\b(?:wearing|holding|carrying|attuned|wielding|on your person)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemAttunedDefenseRegex();

    [GeneratedRegex(@"\b(?:until you finish|until the end|for \{@dice|for \d+ (?:rounds?|minutes?|hours?|days?)|lasts for|for the duration|until your next|until you complete|while .* lasts)\b.*\b(?:resistance|resistant|immunity|immune|vulnerability|vulnerable)\b|\b(?:resistance|resistant|immunity|immune|vulnerability|vulnerable)\b.*\b(?:until you finish|until the end|for \{@dice|for \d+ (?:rounds?|minutes?|hours?|days?)|lasts for|for the duration|until your next|until you complete|while .* lasts)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryDefenseRegex();

    [GeneratedRegex(@"\b(?:while|when|whenever|until|during|as long as|if you are|if the|against|from attacks made by|from .* damage dealt by|provided by)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalDefenseRegex();

    [GeneratedRegex(@"\bresistance to (?:the )?damage dealt by traps\b|\bresistant to (?:the )?damage dealt by traps\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrapDamageResistanceRegex();

    [GeneratedRegex(@"\b(?:you have|you gain|you are granted|you also have)\b\s+(?:\{@variantrule\s+)?(?:resistance|Resistance|resistant)\b.*\b(?:acid|bludgeoning|cold|fire|force|lightning|necrotic|piercing|poison|psychic|radiant|slashing|thunder|damage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PermanentDamageResistanceRegex();

    [GeneratedRegex(@"\b(?:(?:your\s+)?(?:Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+score\s+increases?\s+by\s+\d+|(?:increase|increases?)\s+(?:your\s+)?(?:(?:Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)(?:\s*,\s*|\s+or\s+|\s+and\s+)?){1,}\s+score\s+by\s+\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityTextRegex();

    [GeneratedRegex(@"\b(spell|cantrip|spellcasting|spell list)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellTextRegex();

    [GeneratedRegex(@"^\s*(?:spellcasting|spells?|cantrips?|spell level)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellHeadingRegex();

    [GeneratedRegex(@"\b(?:consider customizing how your spells look|your magic often|your spells tend to|is a favorite of .* spellcasters|oh, yeah, that spell|any spellcasting class or subclass can work well|learning a new spell or adopting|your character's life was changed by a \{@item Deck of Many Things|when your magic causes)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellFlavorRegex();

    [GeneratedRegex(@"\b(?:your magic is meant to|your magic might|as you cast your spells|using the \{@spell .* cantrip|spellcasters often|flavor of your spells|appearance of your spells|when your magic causes)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellFlavorReferenceRegex();

    [GeneratedRegex(@"^\s*(?:\d+(?:st|nd|rd|th)-level spell|level \d+ spell)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellLevelHeadingRegex();

    [GeneratedRegex(@"^\s*(?:\{@spell\s+[^}]+}\s*(?:,|and)?\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellReferenceListRegex();

    [GeneratedRegex(@"\bspellcasting ability\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellcastingAbilityRegex();

    [GeneratedRegex(@"\bprerequisite:\s*(?:spellcasting|pact magic)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellcastingPrerequisiteRegex();

    [GeneratedRegex(@"\b(?:spells on the .* table are added to (?:the|that feature's) spell list|added to the spell list of your spellcasting class|add .* spells? to your spell list)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellListAccessRegex();

    [GeneratedRegex(@"\b(?:spellcasting focus|spellcasting implement|arcane focus|druidic focus|holy symbol)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellcastingFocusRegex();

    [GeneratedRegex(@"\b(?:spell slot|spell slots|regain .* spell slot|extra spell slot|using a .* spell slot|slot's level|slot level|expended charges? .* spell slot)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellSlotRuleRegex();

    [GeneratedRegex(@"^\s*Spell Slots(?: per Spell Level)?\s*$|^\s*(?:1st|2nd|3rd|4th|5th|6th|7th|8th|9th)[-\s]?level(?: Spell)? Slots?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellSlotTableRegex();

    [GeneratedRegex(@"\b(?:spellbook|spell book|ritual book|recorded|transcribed|copy .* spell|add .* spell .* book|contains the following spells)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellbookRegex();

    [GeneratedRegex(@"\b(?:you know|you also know|also know|you learn|you can cast|can cast|always have .* prepared|have .* spell prepared|cast .* without a spell slot|cast .* with this trait|regain the ability to do so|regain all expended uses|once you cast|starting at .* level, you can cast)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InnateSpellGrantRegex();

    [GeneratedRegex(@"\b(?:when you reach|at|starting at)\s+(?:character\s+)?level\s+\d+|(?:when you reach|at|starting at)\s+\d+(?:st|nd|rd|th)\s+level\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelGatedSpellGrantRegex();

    [GeneratedRegex(@"\b(?:you learn|choose|always have|have .* prepared|you can cast)\b.*\b(?:spell|cantrip|{@spell|{@filter[^}]*spells)\b.*\b(?:without (?:expending|a) spell slot|without spending a spell slot|once without a spell slot|finish a long rest|regain the ability to cast it)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FreeCastSpellGrantRegex();

    [GeneratedRegex(@"\b(?:expend|spend|use)\s+(?:a|one|\d+|an)\s+spell slots?\b.*\b(?:roll|damage|heal|restore|protective|effect|target|creature|bonus|reaction)\b|\b(?:reaction|bonus action|action)\b.*\bexpend\s+(?:a|one|\d+|an)\s+spell slots?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellSlotExpenditureEffectRegex();

    [GeneratedRegex(@"\b(?:when you cast|whenever you cast|spell you cast|spells you cast|spell attacks?|spell attack rolls?|spell save DC|spell damage|spell components?|requires no spell components?|concentration on (?:it|a spell)|maintain .* concentration|ignore resistance|reroll .* spell|restore .* with a spell|triggering spell|chosen spell list|replace one of the spells)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellModifierRegex();

    [GeneratedRegex(@"\b(?:charges?|expend|while holding|while wearing|while attuned|as an action|bonus action|command word|study .* at the end of a long rest|cast .* from the item|use .* to cast|use .* and cast|allows you to cast|granted the ability to cast|gain the ability to cast|cast \{@spell|casts? \{@spell|gain the effect of the \{@spell|from the .* and cast|pressing .* you cast|summoned as if you had cast)\b.*\b(?:spell|cantrip|spell slot|{@spell)\b|^\s*cast\s+\{@spell\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemSpellActivationRegex();

    [GeneratedRegex(@"\b(?:bonus to|add .* bonus to|increase .* by)\b.*\b(?:spell attack rolls?|spell save DCs?|saving throw DCs? of your .* spells?|spellcasting ability checks?|spellcasting checks?)\b|\b(?:spell attack rolls?|spell save DCs?|saving throw DCs? of your .* spells?|spellcasting ability checks?|spellcasting checks?)\b.*\b(?:bonus|increase)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemSpellcastingBonusRegex();

    [GeneratedRegex(@"\b(?:choose|replace|prepare|prepared|learn|know)\b.*\b(?:cantrip|spell)\b.*\b(?:for \d+ hours?|until|at the end of a long rest|long rest|different spell|class list|spell list)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemTemporarySpellGrantRegex();

    [GeneratedRegex(@"\b(?:expend|spend|uses?)\s+\d+\s+charges?\b.*\b(?:cast|spell|{@spell)\b|\b(?:charges?|charge)\b.*\b(?:cast|casts|spell|{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemChargeSpellActivationRegex();

    [GeneratedRegex(@"\b(?:use|take|as)\s+(?:your\s+)?(?:an?\s+)?(?:action|bonus action|\{@action Magic\|XPHB} action|Magic action)\b.*\b(?:cast|casts|{@spell)\b|\b(?:use|take)\s+(?:your\s+)?(?:an?\s+)?(?:action|bonus action|\{@action Magic\|XPHB} action|Magic action)\s+to\b.*\b(?:cast|{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemActionSpellActivationRegex();

    [GeneratedRegex(@"\b(?:cast|casts|use .* to cast)\b.*\b(?:as a ritual|ritual)\b|\b(?:as a ritual|ritual)\b.*\b(?:cast|{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemRitualSpellActivationRegex();

    [GeneratedRegex(@"\b(?:allows you to cast|allow you to cast|can use it to cast|can use .* to cast|used to cast a given spell|you can cast \{@spell|you may cast \{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemPassiveSpellAccessRegex();

    [GeneratedRegex(@"\b(?:had (?:the )?\{@spell [^}]+} spell cast on it|spell cast on it|spell cast on them|affected by (?:the )?\{@spell|as if affected by (?:the )?\{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellAffectedObjectReferenceRegex();

    [GeneratedRegex(@"\b(?:removed with|remove(?:d)? by|ended with|ended only with|spell can end|spell ends|dispel magic|remove curse|greater restoration|lesser restoration|similar magic)\b.*\b(?:spell|magic|curse|effect|condition|{@spell)\b|\b\{@spell (?:remove curse|greater restoration|lesser restoration|dispel magic)[^}]*}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellRemovalReferenceRegex();

    [GeneratedRegex(@"\b(?:fails? a saving throw against a spell|saving throw against .* spell|damage .* from .* spell|spell that deals damage|attack, spell, or feature|from that spell|spell-created|can't cast spells)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellCombatInteractionRegex();

    [GeneratedRegex(@"\b(?:attack or a spell|attack or spell|spell or effect|targeted by the spell|target of the .* spell|affected by the spell|benefit from several spells|cast on you|when .* casts? a spell|whenever .* casts? a spell|spell attack|spell save|spell damage|somatic components of a spell|concentrating on a spell|spell ends|spell takes effect|only .* spell can|spell or similar|as if affected by|provides no defense against|targeted by a spell that ends a curse|spells and other magical effects|spell can end|spell is sufficient|per the \{@spell|as per the \{@spell)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellEffectReferenceRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private static readonly TextDetector[] TextDetectors =
    [
        new(ChoiceTextRegex(), "choice-candidate", "candidate", 0.75, "Text appears to require a user choice.", "ChoiceTextParser"),
        new(ProficiencyTextRegex(), "proficiency-candidate", "candidate", 0.7, "Text appears to grant or modify proficiency/expertise.", "ProficiencyTextParser"),
        new(DefenseTextRegex(), "defense-candidate", "candidate", 0.7, "Text appears to grant or modify resistance, immunity, or vulnerability.", "DefenseTextParser"),
        new(AbilityTextRegex(), "ability-score-candidate", "candidate", 0.72, "Text appears to grant or modify ability scores.", "AbilityRuleParser"),
        new(SpellTextRegex(), "spell-rule-candidate", "candidate", 0.55, "Text appears to grant or modify spellcasting or spell access.", "SpellGrantParser")
    ];

    private sealed class DataQualityContext(string sourceDataPath)
    {
        private readonly Dictionary<string, List<string>> duplicateLookup = new(StringComparer.OrdinalIgnoreCase);

        public string SourceDataPath { get; } = Path.GetFullPath(sourceDataPath);
        public List<UnhandledCase> Cases { get; } = [];
        public Dictionary<string, int> EntityCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Count(string category)
        {
            EntityCounts[category] = EntityCounts.GetValueOrDefault(category) + 1;
        }

        public void AddCase(UnhandledCase item)
        {
            Cases.Add(item);
        }

        public void TrackDuplicate(string category, string name, string source, string path)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var key = $"{category}|{name}|{source}";
            if (!duplicateLookup.TryGetValue(key, out var paths))
            {
                paths = [];
                duplicateLookup[key] = paths;
            }

            paths.Add(path);
        }

        public void FinalizeDuplicateChecks()
        {
            foreach (var pair in duplicateLookup.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key))
            {
                var parts = pair.Key.Split('|');
                var hasFoundryOverlay = pair.Value.Any(path => path.Contains("foundry.json", StringComparison.OrdinalIgnoreCase));
                var caseType = hasFoundryOverlay ? "foundry-overlay-duplicate" : "duplicate-source-version";
                var reason = hasFoundryOverlay
                    ? $"Found {pair.Value.Count} entries with the same category, name, and source, including a Foundry overlay entry."
                    : $"Found {pair.Value.Count} entries with the same category, name, and source.";
                var suggestedParser = hasFoundryOverlay ? "FoundryOverlayGrouper" : "SourceVersionGrouper";

                AddCase(new UnhandledCase(
                    parts[0],
                    parts.Length > 1 ? parts[1] : "",
                    parts.Length > 2 ? parts[2] : "",
                    string.Join(", ", pair.Value.Take(5)),
                    caseType,
                    "warning",
                    null,
                    reason,
                    null,
                    suggestedParser));
            }
        }
    }

    private sealed record TextDetector(
        Regex Pattern,
        string CaseType,
        string Severity,
        double Confidence,
        string Reason,
        string SuggestedParser)
    {
        public TextCaseInfo ToCaseInfo()
        {
            return new TextCaseInfo(CaseType, Severity, Confidence, Reason, SuggestedParser);
        }
    }

    private sealed record TextCaseInfo(
        string CaseType,
        string Severity,
        double Confidence,
        string Reason,
        string SuggestedParser);
}

internal sealed record DataQualityReportResult(
    string MarkdownPath,
    string JsonPath,
    string UnhandledMarkdownPath,
    int CaseCount);

internal sealed record UnhandledCase(
    string Category,
    string Name,
    string Source,
    string Path,
    string CaseType,
    string Severity,
    double? Confidence,
    string Reason,
    string? TextPreview,
    string? SuggestedParser);
