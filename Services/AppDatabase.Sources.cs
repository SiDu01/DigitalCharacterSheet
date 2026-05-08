using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<SourceSetting>> GetSourceSettingsAsync()
    {
        await InitializeAsync();
        await EnsureSourceSettingsAsync();

        var entities = await _database.Table<SourceSettingEntity>()
            .OrderBy(setting => setting.DisplayPriority)
            .ThenBy(setting => setting.SourceCode)
            .ToListAsync();
        return entities.Select(ToModel).ToList();
    }

    public async Task SetSourceEnabledAsync(string sourceCode, bool isEnabled)
    {
        await InitializeAsync();

        var entity = await _database.FindAsync<SourceSettingEntity>(sourceCode);
        if (entity is null)
        {
            return;
        }

        entity.IsEnabled = isEnabled;
        await _database.UpdateAsync(entity);
    }

    public async Task MoveSourceAsync(string sourceCode, int direction)
    {
        await InitializeAsync();

        var settings = await _database.Table<SourceSettingEntity>()
            .OrderBy(setting => setting.DisplayPriority)
            .ThenBy(setting => setting.SourceCode)
            .ToListAsync();
        var currentIndex = settings.FindIndex(setting => string.Equals(setting.SourceCode, sourceCode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(currentIndex + direction, 0, settings.Count - 1);
        if (targetIndex == currentIndex)
        {
            return;
        }

        (settings[currentIndex], settings[targetIndex]) = (settings[targetIndex], settings[currentIndex]);
        for (var index = 0; index < settings.Count; index++)
        {
            settings[index].DisplayPriority = index;
            await _database.UpdateAsync(settings[index]);
        }
    }

    public async Task ResetSourceSettingsAsync()
    {
        await InitializeAsync();

        var sources = await GetAvailableSourceCodesAsync();
        var settings = sources
            .OrderBy(DefaultSourcePriority)
            .ThenBy(source => source, StringComparer.OrdinalIgnoreCase)
            .Select((source, index) => new SourceSettingEntity
            {
                SourceCode = source,
                DisplayName = source,
                IsEnabled = true,
                DisplayPriority = index
            })
            .ToList();

        await _database.DeleteAllAsync<SourceSettingEntity>();
        await _database.InsertAllAsync(settings);
    }

    private async Task EnsureSourceSettingsAsync()
    {
        var sourceCodes = await GetAvailableSourceCodesAsync();
        var existingSettings = await _database.Table<SourceSettingEntity>().ToListAsync();
        var existingBySource = existingSettings.ToDictionary(setting => setting.SourceCode, StringComparer.OrdinalIgnoreCase);
        var nextPriority = existingSettings.Count == 0
            ? 0
            : existingSettings.Max(setting => setting.DisplayPriority) + 1;

        foreach (var sourceCode in sourceCodes.OrderBy(DefaultSourcePriority).ThenBy(source => source, StringComparer.OrdinalIgnoreCase))
        {
            if (existingBySource.ContainsKey(sourceCode))
            {
                continue;
            }

            await _database.InsertAsync(new SourceSettingEntity
            {
                SourceCode = sourceCode,
                DisplayName = sourceCode,
                IsEnabled = true,
                DisplayPriority = nextPriority++
            });
        }
    }

    private async Task<HashSet<string>> GetEnabledSourceCodesInternalAsync()
    {
        var settings = await _database.Table<SourceSettingEntity>().ToListAsync();
        if (settings.Count == 0)
        {
            return (await GetAvailableSourceCodesAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return settings
            .Where(setting => setting.IsEnabled)
            .Select(setting => setting.SourceCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetSourcePriorityMapAsync()
    {
        await InitializeAsync();
        return await GetSourcePriorityMapInternalAsync();
    }

    private async Task<IReadOnlyDictionary<string, int>> GetSourcePriorityMapInternalAsync()
    {
        var settings = await _database.Table<SourceSettingEntity>()
            .OrderBy(setting => setting.DisplayPriority)
            .ThenBy(setting => setting.SourceCode)
            .ToListAsync();

        return settings.ToDictionary(setting => setting.SourceCode, setting => setting.DisplayPriority, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> GetAvailableSourceCodesAsync()
    {
        var sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSources(sources, (await _database.Table<SpellEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<ClassDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<SubclassDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<RaceDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<SubraceDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<BackgroundDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<FeatDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<ItemDefinitionEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<ItemGroupEntity>().ToListAsync()).Select(entity => entity.Source));
        AddSources(sources, (await _database.Table<MagicItemVariantEntity>().ToListAsync()).Select(entity => entity.Source));

        return sources.ToList();
    }

    private static void AddSources(ISet<string> sources, IEnumerable<string> sourceCodes)
    {
        foreach (var sourceCode in sourceCodes)
        {
            if (!string.IsNullOrWhiteSpace(sourceCode))
            {
                sources.Add(sourceCode.Trim());
            }
        }
    }

    private static int DefaultSourcePriority(string source)
    {
        return source switch
        {
            "XPHB" => 0,
            "PHB" => 1,
            _ => 1000
        };
    }
}
