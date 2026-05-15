using System.Text.Json;

namespace DigitalCharacterSheet.Models;

public sealed class WikiEntry
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Slug { get; set; } = "";
    public int? Page { get; set; }
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public string EntriesJson { get; set; } = "";
    public string RawJson { get; set; } = "";

    public IReadOnlyList<string> Paragraphs => ParseEntries(EntriesJson).ToList();

    private static IEnumerable<string> ParseEntries(string entriesJson)
    {
        if (string.IsNullOrWhiteSpace(entriesJson))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(entriesJson);
        foreach (var paragraph in ExtractParagraphs(document.RootElement))
        {
            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                yield return paragraph;
            }
        }
    }

    private static IEnumerable<string> ExtractParagraphs(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString() ?? "";
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var name = ReadString(element, "name");
            var nested = new List<string>();
            foreach (var propertyName in new[] { "entry", "entries", "items" })
            {
                if (element.TryGetProperty(propertyName, out var property))
                {
                    nested.AddRange(ExtractParagraphs(property));
                }
            }

            if (!string.IsNullOrWhiteSpace(name) && nested.Count > 0)
            {
                yield return $"{name}: {string.Join(" ", nested)}";
                yield break;
            }

            foreach (var value in nested)
            {
                yield return value;
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in element.EnumerateArray())
        {
            foreach (var value in ExtractParagraphs(item))
            {
                yield return value;
            }
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }
}

public sealed class WikiEntryGroup
{
    public WikiEntryGroup(IEnumerable<WikiEntry> versions)
    {
        Versions = versions
            .OrderBy(version => version.Source)
            .ThenBy(version => version.Name)
            .ToList();
        Primary = Versions.First();
        Name = Primary.Name;
        Category = Primary.Category;
    }

    public string Name { get; }
    public string Category { get; }
    public WikiEntry Primary { get; }
    public IReadOnlyList<WikiEntry> Versions { get; }

    public WikiEntry ResolveVersion(WikiEntry? selected)
    {
        return selected is not null && Versions.Any(version => version.Id == selected.Id)
            ? selected
            : Primary;
    }
}
