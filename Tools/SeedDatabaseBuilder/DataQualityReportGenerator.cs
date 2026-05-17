using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
            context.AddCase(new UnhandledCase(category, name, source, path, "missing-name", "error", null, "Entity has no name.", null, null));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            context.AddCase(new UnhandledCase(category, name, source, path, "missing-source", "warning", null, "Entity has no source.", null, null));
        }

        if (category is "race-version")
        {
            var raceName = ReadString(element, "raceName");
            var raceSource = ReadString(element, "raceSource");
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

        if (category is "feat" && element.TryGetProperty("repeatable", out var repeatable) && repeatable.ValueKind is JsonValueKind.True)
        {
            context.AddCase(new UnhandledCase(category, name, source, path, "repeatable-feat", "candidate", 1.0, "Feat is repeatable and needs instance-aware choice/effect handling.", null, "RepeatableFeatParser"));
        }

        if (category is "class")
        {
            AnalyzeClassSubclassGrantLevels(context, category, name, source, path, element);
        }
    }

    private static void AnalyzeClassSubclassGrantLevels(DataQualityContext context, string category, string name, string source, string path, JsonElement element)
    {
        if (!element.TryGetProperty("classFeatures", out var features) || features.ValueKind != JsonValueKind.Array)
        {
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

        foreach (var detector in TextDetectors)
        {
            if (!detector.Pattern.IsMatch(text))
            {
                continue;
            }

            context.AddCase(new UnhandledCase(
                category,
                name,
                source,
                path,
                detector.CaseType,
                detector.Severity,
                detector.Confidence,
                detector.Reason,
                Preview(text),
                detector.SuggestedParser));
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
            "choice-candidate" => 80,
            "proficiency-candidate" => 72,
            "defense-candidate" => 62,
            "no-subclass-grant-levels" => 58,
            "duplicate-source-version" => 48,
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
            "choice-candidate" => 5,
            "proficiency-candidate" => 6,
            "defense-candidate" => 7,
            "no-subclass-grant-levels" => 8,
            "duplicate-source-version" => 9,
            "spell-rule-candidate" => 10,
            _ => 20
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

    [GeneratedRegex(@"\b(choose|select|pick)\s+(one|two|three|a|an|any|from|\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChoiceTextRegex();

    [GeneratedRegex(@"\b(proficiency|proficiencies|expertise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProficiencyTextRegex();

    [GeneratedRegex(@"\b(resistance|resistant|immunity|immune|vulnerability|vulnerable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefenseTextRegex();

    [GeneratedRegex(@"\b(add|increase|increases?)\s+(your\s+)?(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma|ability score)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityTextRegex();

    [GeneratedRegex(@"\b(spell|cantrip|spellcasting|spell list)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpellTextRegex();

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
                AddCase(new UnhandledCase(
                    parts[0],
                    parts.Length > 1 ? parts[1] : "",
                    parts.Length > 2 ? parts[2] : "",
                    string.Join(", ", pair.Value.Take(5)),
                    "duplicate-source-version",
                    "warning",
                    null,
                    $"Found {pair.Value.Count} entries with the same category, name, and source.",
                    null,
                    "SourceVersionGrouper"));
            }
        }
    }

    private sealed record TextDetector(
        Regex Pattern,
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
