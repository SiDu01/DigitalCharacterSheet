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
        builder.AppendLine();
        builder.AppendLine("## Top Cases");
        builder.AppendLine();
        foreach (var item in context.Cases.Take(100))
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
        foreach (var group in cases.GroupBy(item => item.CaseType).OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
        {
            builder.AppendLine($"## {group.Key} ({group.Count()})");
            builder.AppendLine();
            foreach (var item in group.Take(100))
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
