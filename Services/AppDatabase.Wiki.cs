using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<WikiEntry>> GetWikiEntriesAsync()
    {
        await InitializeAsync();

        var rows = await _database.Table<WikiEntryEntity>()
            .OrderBy(entry => entry.Category)
            .ThenBy(entry => entry.Name)
            .ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task EnsureWikiDataImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("WikiImportVersion");
        var wikiCount = await _database.Table<WikiEntryEntity>().CountAsync();
        if (import?.Value == WikiImportVersion && wikiCount > 0)
        {
            return;
        }

#if !SEED_BUILDER
        if (_useSeedDatabase)
        {
            await TryRefreshWikiReferenceDataFromSeedAsync();
            return;
        }
#endif

#if SEED_BUILDER
        await _database.DeleteAllAsync<WikiEntryEntity>();
        await ImportWikiFileAsync("conditionsdiseases.json", "condition", "Conditions", "Condition");
        await ImportWikiFileAsync("conditionsdiseases.json", "disease", "Conditions", "Disease");
        await ImportWikiFileAsync("actions.json", "action", "Rules", "Action");
        await ImportWikiFileAsync("variantrules.json", "variantrule", "Rules", "Variant Rule");
        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = "WikiImportVersion", Value = WikiImportVersion });
#else
        await Task.CompletedTask;
#endif
    }

#if !SEED_BUILDER
    private async Task TryRefreshWikiReferenceDataFromSeedAsync()
    {
        var seedPath = await CopySeedDatabaseToTemporaryFileAsync();
        var escapedSeedPath = seedPath.Replace("'", "''", StringComparison.Ordinal);

        try
        {
            await _database.ExecuteAsync($"ATTACH DATABASE '{escapedSeedPath}' AS seeddb");
            var seedWikiVersion = await _database.ExecuteScalarAsync<string>(
                "SELECT Value FROM seeddb.DatabaseMetadata WHERE Key = 'WikiImportVersion'");
            if (!string.Equals(seedWikiVersion, WikiImportVersion, StringComparison.Ordinal))
            {
                return;
            }

            await _database.ExecuteAsync("DELETE FROM WikiEntries");
            await _database.ExecuteAsync("""
                INSERT INTO WikiEntries (Id, Category, Name, Source, Slug, Page, Type, Summary, EntriesJson, RawJson)
                SELECT Id, Category, Name, Source, Slug, Page, Type, Summary, EntriesJson, RawJson
                FROM seeddb.WikiEntries
                """);
            await SetMetadataAsync("WikiImportVersion", WikiImportVersion);
        }
        finally
        {
            try
            {
                await _database.ExecuteAsync("DETACH DATABASE seeddb");
            }
            catch
            {
            }

            TryDeleteFile(seedPath);
        }
    }
#endif

#if SEED_BUILDER
    private async Task ImportWikiFileAsync(string fileName, string arrayName, string category, string type)
    {
        var path = Path.Combine(seedSourceDataPath, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        if (!document.RootElement.TryGetProperty(arrayName, out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            var name = ReadString(entry, "name");
            var source = ReadString(entry, "source");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var entriesJson = ReadRawJson(entry, "entries");
            await _database.InsertAsync(new WikiEntryEntity
            {
                Category = category,
                Name = name,
                Source = source,
                Slug = $"{NormalizeSlugPart(category)}|{BuildSlug(name, source)}",
                Page = ReadInt(entry, "page"),
                Type = type,
                Summary = BuildWikiSummary(entriesJson),
                EntriesJson = entriesJson,
                RawJson = entry.GetRawText()
            });
        }
    }
#endif

    private static string BuildWikiSummary(string entriesJson)
    {
        if (string.IsNullOrWhiteSpace(entriesJson))
        {
            return "";
        }

        using var document = JsonDocument.Parse(entriesJson);
        var first = ExtractWikiParagraphs(document.RootElement).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
        return first.Length <= 180 ? first : $"{first[..177]}...";
    }

    private static IEnumerable<string> ExtractWikiParagraphs(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString() ?? "";
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "entry", "entries", "items" })
            {
                if (!element.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                foreach (var value in ExtractWikiParagraphs(property))
                {
                    yield return value;
                }
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in element.EnumerateArray())
        {
            foreach (var value in ExtractWikiParagraphs(item))
            {
                yield return value;
            }
        }
    }

    private static WikiEntry ToModel(WikiEntryEntity entity)
    {
        return new WikiEntry
        {
            Id = entity.Id,
            Category = entity.Category,
            Name = entity.Name,
            Source = entity.Source,
            Slug = entity.Slug,
            Page = entity.Page,
            Type = entity.Type,
            Summary = entity.Summary,
            EntriesJson = entity.EntriesJson,
            RawJson = entity.RawJson
        };
    }
}
