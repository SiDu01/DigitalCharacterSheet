using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<Spell>> GetSpellsAsync(SpellFilter? filter = null)
    {
        await InitializeAsync();

        var enabledSources = await GetEnabledSourceCodesInternalAsync();
        var entities = await _database.Table<SpellEntity>().ToListAsync();
        var tags = await _database.Table<SpellTagEntity>().ToListAsync();
        var tagsBySpellId = tags
            .GroupBy(tag => tag.SpellId)
            .ToDictionary(group => group.Key, group => group.AsEnumerable());
        var spells = entities
            .Where(entity => enabledSources.Contains(entity.Source))
            .Select(entity => ToModel(entity, tagsBySpellId.GetValueOrDefault(entity.Id) ?? []))
            .ToList();

        return ApplyFilter(spells, filter).ToList();
    }

    public async Task<IReadOnlyList<Spell>> GetSpellSummariesAsync(SpellFilter? filter = null)
    {
        await InitializeAsync();

        var enabledSources = await GetEnabledSourceCodesInternalAsync();
        var entities = await _database.QueryAsync<SpellEntity>(
            """
            SELECT
                Id,
                Name,
                Source,
                Page,
                Level,
                SchoolCode,
                School,
                CastingTimeNumber,
                CastingTimeUnit,
                CastingTimeCondition,
                CastingTimeNote,
                RangeType,
                RangeDistanceType,
                RangeDistanceAmount,
                HasVerbalComponent,
                HasSomaticComponent,
                HasMaterialComponent,
                MaterialComponent,
                MaterialCostCopper,
                ConsumesMaterial,
                HasRoyaltyComponent,
                DurationType,
                DurationAmount,
                DurationUnit,
                DurationEnds,
                RequiresConcentration,
                IsRitual,
                IsPrepared,
                IsFavorite,
                IsSrd,
                IsBasicRules,
                IsSrd2024,
                IsBasicRules2024,
                HasFluff,
                HasFluffImages
            FROM Spells
            ORDER BY Level, Name, Source
            """);
        entities = entities.Where(entity => enabledSources.Contains(entity.Source)).ToList();

        var spellIds = entities.Select(entity => entity.Id).ToHashSet();
        var tags = (await _database.Table<SpellTagEntity>().ToListAsync())
            .Where(tag => spellIds.Contains(tag.SpellId))
            .GroupBy(tag => tag.SpellId)
            .ToDictionary(group => group.Key, group => group.AsEnumerable());

        var spells = entities.Select(entity => ToModel(entity, tags.GetValueOrDefault(entity.Id) ?? [])).ToList();
        return ApplyFilter(spells, filter).ToList();
    }

    public async Task<IReadOnlyList<Spell>> GetSpellVersionsAsync(string name)
    {
        await InitializeAsync();

        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var enabledSources = await GetEnabledSourceCodesInternalAsync();
        var sourcePriorities = await GetSourcePriorityMapInternalAsync();
        var entities = await _database.Table<SpellEntity>()
            .Where(spell => spell.Name == name)
            .ToListAsync();
        entities = entities.Where(entity => enabledSources.Contains(entity.Source)).ToList();
        var spellIds = entities.Select(entity => entity.Id).ToHashSet();
        var tags = (await _database.Table<SpellTagEntity>().ToListAsync())
            .Where(tag => spellIds.Contains(tag.SpellId))
            .GroupBy(tag => tag.SpellId)
            .ToDictionary(group => group.Key, group => group.AsEnumerable());

        return entities
            .Select(entity => ToModel(entity, tags.GetValueOrDefault(entity.Id) ?? []))
            .OrderBy(spell => sourcePriorities.TryGetValue(spell.Source, out var priority) ? priority : 1000)
            .ThenBy(spell => spell.Source)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetSpellcastingClassNamesAsync()
    {
        await InitializeAsync();

        var accessRules = await _database.Table<ClassSpellAccessRuleEntity>().ToListAsync();
        var classIds = accessRules.Select(rule => rule.ClassDefinitionId).ToHashSet();
        var classes = await _database.Table<ClassDefinitionEntity>().ToListAsync();

        return classes
            .Where(classDefinition => classIds.Contains(classDefinition.Id))
            .Select(classDefinition => classDefinition.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, HashSet<string>>> GetSpellNamesByClassAsync()
    {
        await InitializeAsync();

        var rules = await _database.Table<ClassSpellAccessRuleEntity>().ToListAsync();
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var spells = await _database.Table<SpellEntity>().ToListAsync();
        var classesById = classDefinitions.ToDictionary(classDefinition => classDefinition.Id);
        var spellsById = spells.ToDictionary(spell => spell.Id);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (!classesById.TryGetValue(rule.ClassDefinitionId, out var classDefinition)
                || !spellsById.TryGetValue(rule.SpellId, out var spell))
            {
                continue;
            }

            if (!result.TryGetValue(classDefinition.Name, out var spellNames))
            {
                spellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[classDefinition.Name] = spellNames;
            }

            spellNames.Add(spell.Name);
        }

        return result;
    }

    public async Task<Spell?> GetSpellAsync(int id)
    {
        await InitializeAsync();

        var entity = await _database.Table<SpellEntity>().Where(spell => spell.Id == id).FirstOrDefaultAsync();
        if (entity is null)
        {
            return null;
        }

        var tags = await _database.Table<SpellTagEntity>().Where(tag => tag.SpellId == id).ToListAsync();
        return ToModel(entity, tags);
    }

    public async Task SetPreparedAsync(int id, bool isPrepared)
    {
        await InitializeAsync();

        var entity = await _database.Table<SpellEntity>().Where(spell => spell.Id == id).FirstOrDefaultAsync();
        if (entity is null)
        {
            return;
        }

        entity.IsPrepared = isPrepared;
        await _database.UpdateAsync(entity);
    }

    public async Task SetFavoriteAsync(int id, bool isFavorite)
    {
        await InitializeAsync();

        var entity = await _database.Table<SpellEntity>().Where(spell => spell.Id == id).FirstOrDefaultAsync();
        if (entity is null)
        {
            return;
        }

        entity.IsFavorite = isFavorite;
        await _database.UpdateAsync(entity);
    }

    private async Task EnsureImportedAsync()
    {
        var schema = await _database.FindAsync<DatabaseMetadata>("SchemaVersion");
        var import = await _database.FindAsync<DatabaseMetadata>("ImportVersion");
        var count = await _database.Table<SpellEntity>().CountAsync();

        if (schema?.Value == SchemaVersion.ToString()
            && import?.Value == ImportVersion
            && count > 0)
        {
            return;
        }

#if !SEED_BUILDER
        if (_useSeedDatabase)
        {
            throw BuildReferenceDataVersionMismatchException("spell", "ImportVersion", import?.Value, ImportVersion);
        }
#endif

        var spells = await _importService.LoadBundledSpellsAsync();

        await _database.RunInTransactionAsync(connection =>
        {
            connection.DeleteAll<SpellTagEntity>();
            connection.DeleteAll<SpellEntity>();

            foreach (var spell in spells)
            {
                var entity = ToEntity(spell);
                connection.Insert(entity);

                var tags = CreateTags(entity.Id, spell);
                if (tags.Count > 0)
                {
                    connection.InsertAll(tags);
                }
            }

            connection.InsertOrReplace(new DatabaseMetadata { Key = "SchemaVersion", Value = SchemaVersion.ToString() });
            connection.InsertOrReplace(new DatabaseMetadata { Key = "ImportVersion", Value = ImportVersion });
        });
    }

    private async Task EnsureSpellAccessImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("SpellAccessImportVersion");
        var count = await _database.Table<ClassSpellAccessRuleEntity>().CountAsync();

        if (import?.Value == SpellAccessImportVersion && count > 0)
        {
            return;
        }

#if !SEED_BUILDER
        if (_useSeedDatabase)
        {
            throw BuildReferenceDataVersionMismatchException(
                "spell access",
                "SpellAccessImportVersion",
                import?.Value,
                SpellAccessImportVersion);
        }
#endif

        await _database.DeleteAllAsync<ClassSpellAccessRuleEntity>();

        var spells = await _database.Table<SpellEntity>().ToListAsync();
        var spellsByKey = spells.ToDictionary(spell => $"{spell.Source}|{spell.Name}", StringComparer.OrdinalIgnoreCase);
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var classesByKey = classDefinitions.ToDictionary(classDefinition => $"{classDefinition.Name}|{classDefinition.Source}", StringComparer.OrdinalIgnoreCase);

        await using var stream = await OpenAssetAsync("spells/sources.json");
        using var document = await JsonDocument.ParseAsync(stream);

        var rules = new List<ClassSpellAccessRuleEntity>();
        foreach (var spellSourceProperty in document.RootElement.EnumerateObject())
        {
            var spellSource = spellSourceProperty.Name;
            foreach (var spellProperty in spellSourceProperty.Value.EnumerateObject())
            {
                if (!spellsByKey.TryGetValue($"{spellSource}|{spellProperty.Name}", out var spell))
                {
                    continue;
                }

                AddClassAccessRules(rules, spellProperty.Value, "class", spell, spellSource, classesByKey);
                AddClassAccessRules(rules, spellProperty.Value, "classVariant", spell, spellSource, classesByKey);
            }
        }

        if (rules.Count > 0)
        {
            await _database.InsertAllAsync(rules);
        }

        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = "SpellAccessImportVersion", Value = SpellAccessImportVersion });
    }

    private static void AddClassAccessRules(
        List<ClassSpellAccessRuleEntity> rules,
        JsonElement spellAccess,
        string propertyName,
        SpellEntity spell,
        string spellSource,
        IReadOnlyDictionary<string, ClassDefinitionEntity> classesByKey)
    {
        if (!spellAccess.TryGetProperty(propertyName, out var classArray) || classArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var classElement in classArray.EnumerateArray())
        {
            var className = ReadString(classElement, "name");
            var classSource = ReadString(classElement, "source");
            if (!classesByKey.TryGetValue($"{className}|{classSource}", out var classDefinition))
            {
                continue;
            }

            var definedInSource = ReadString(classElement, "definedInSource");
            rules.Add(new ClassSpellAccessRuleEntity
            {
                SpellId = spell.Id,
                ClassDefinitionId = classDefinition.Id,
                AccessSource = string.IsNullOrWhiteSpace(definedInSource) ? spellSource : definedInSource
            });
        }
    }

    private async Task InsertTagsAsync(int spellId, Spell spell)
    {
        var tags = CreateTags(spellId, spell);

        if (tags.Count > 0)
        {
            await _database.InsertAllAsync(tags);
        }
    }

    private static List<SpellTagEntity> CreateTags(int spellId, Spell spell)
    {
        var tags = new List<SpellTagEntity>();
        AddTags(tags, spellId, "SavingThrow", spell.SavingThrows);
        AddTags(tags, spellId, "AbilityCheck", spell.AbilityChecks);
        AddTags(tags, spellId, "DamageType", spell.DamageTypes);
        AddTags(tags, spellId, "DamageResistance", spell.DamageResistances);
        AddTags(tags, spellId, "DamageImmunity", spell.DamageImmunities);
        AddTags(tags, spellId, "DamageVulnerability", spell.DamageVulnerabilities);
        AddTags(tags, spellId, "ConditionInflicted", spell.ConditionsInflicted);
        AddTags(tags, spellId, "ConditionImmunity", spell.ConditionImmunities);
        AddTags(tags, spellId, "SpellAttackType", spell.SpellAttackTypes);
        AddTags(tags, spellId, "AffectsCreatureType", spell.AffectsCreatureTypes);
        AddTags(tags, spellId, "MiscTag", spell.MiscTags);
        AddTags(tags, spellId, "AreaTag", spell.AreaTags);
        AddTags(tags, spellId, "Alias", spell.Aliases);

        return tags;
    }

    private static IEnumerable<Spell> ApplyFilter(IEnumerable<Spell> spells, SpellFilter? filter)
    {
        if (filter is null)
        {
            return spells.OrderBy(spell => spell.Level).ThenBy(spell => spell.Name);
        }

        var query = spells;

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var search = filter.SearchText.Trim();
            query = query.Where(spell =>
                spell.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || spell.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || spell.UpCast.Contains(search, StringComparison.OrdinalIgnoreCase)
                || spell.Aliases.Any(alias => alias.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (filter.Level is not null)
        {
            query = query.Where(spell => spell.Level == filter.Level.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.School))
        {
            query = query.Where(spell => spell.School == filter.School);
        }

        if (!string.IsNullOrWhiteSpace(filter.Source))
        {
            query = query.Where(spell => spell.Source == filter.Source);
        }

        if (filter.RequiresConcentration is not null)
        {
            query = query.Where(spell => spell.RequiresConcentration == filter.RequiresConcentration.Value);
        }

        if (filter.IsRitual is not null)
        {
            query = query.Where(spell => spell.IsRitual == filter.IsRitual.Value);
        }

        return query.OrderBy(spell => spell.Level).ThenBy(spell => spell.Name).ThenBy(spell => spell.Source);
    }

    private static void AddTags(List<SpellTagEntity> tags, int spellId, string category, IEnumerable<string> values)
    {
        tags.AddRange(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new SpellTagEntity
            {
                SpellId = spellId,
                Category = category,
                Value = value
            }));
    }

    private static SpellEntity ToEntity(Spell spell)
    {
        return new SpellEntity
        {
            Name = spell.Name,
            Source = spell.Source,
            SourceNameKey = $"{spell.Source}|{spell.Name}".ToLowerInvariant(),
            Page = spell.Page,
            Level = spell.Level,
            SchoolCode = spell.SchoolCode,
            School = spell.School,
            CastingTimeNumber = spell.CastingTimeNumber,
            CastingTimeUnit = spell.CastingTimeUnit,
            CastingTimeCondition = spell.CastingTimeCondition,
            CastingTimeNote = spell.CastingTimeNote,
            RangeType = spell.RangeType,
            RangeDistanceType = spell.RangeDistanceType,
            RangeDistanceAmount = spell.RangeDistanceAmount,
            HasVerbalComponent = spell.HasVerbalComponent,
            HasSomaticComponent = spell.HasSomaticComponent,
            HasMaterialComponent = spell.HasMaterialComponent,
            MaterialComponent = spell.MaterialComponent,
            MaterialCostCopper = spell.MaterialCostCopper,
            ConsumesMaterial = spell.ConsumesMaterial,
            HasRoyaltyComponent = spell.HasRoyaltyComponent,
            DurationType = spell.DurationType,
            DurationAmount = spell.DurationAmount,
            DurationUnit = spell.DurationUnit,
            DurationEnds = spell.DurationEnds,
            RequiresConcentration = spell.RequiresConcentration,
            IsRitual = spell.IsRitual,
            IsPrepared = spell.IsPrepared,
            IsFavorite = spell.IsFavorite,
            Description = spell.Description,
            UpCast = spell.UpCast,
            IsSrd = spell.IsSrd,
            IsBasicRules = spell.IsBasicRules,
            IsSrd2024 = spell.IsSrd2024,
            IsBasicRules2024 = spell.IsBasicRules2024,
            HasFluff = spell.HasFluff,
            HasFluffImages = spell.HasFluffImages,
            RawJson = spell.RawJson
        };
    }

    private static Spell ToModel(SpellEntity entity, IEnumerable<SpellTagEntity> tags)
    {
        var groupedTags = tags
            .GroupBy(tag => tag.Category)
            .ToDictionary(group => group.Key, group => group.Select(tag => tag.Value).ToList());

        return new Spell
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            Level = entity.Level,
            SchoolCode = entity.SchoolCode,
            School = entity.School,
            CastingTimeNumber = entity.CastingTimeNumber,
            CastingTimeUnit = entity.CastingTimeUnit,
            CastingTimeCondition = entity.CastingTimeCondition,
            CastingTimeNote = entity.CastingTimeNote,
            RangeType = entity.RangeType,
            RangeDistanceType = entity.RangeDistanceType,
            RangeDistanceAmount = entity.RangeDistanceAmount,
            HasVerbalComponent = entity.HasVerbalComponent,
            HasSomaticComponent = entity.HasSomaticComponent,
            HasMaterialComponent = entity.HasMaterialComponent,
            MaterialComponent = entity.MaterialComponent,
            MaterialCostCopper = entity.MaterialCostCopper,
            ConsumesMaterial = entity.ConsumesMaterial,
            HasRoyaltyComponent = entity.HasRoyaltyComponent,
            DurationType = entity.DurationType,
            DurationAmount = entity.DurationAmount,
            DurationUnit = entity.DurationUnit,
            DurationEnds = entity.DurationEnds,
            RequiresConcentration = entity.RequiresConcentration,
            IsRitual = entity.IsRitual,
            IsPrepared = entity.IsPrepared,
            IsFavorite = entity.IsFavorite,
            Description = entity.Description,
            UpCast = entity.UpCast,
            SavingThrows = ReadTag(groupedTags, "SavingThrow"),
            AbilityChecks = ReadTag(groupedTags, "AbilityCheck"),
            DamageTypes = ReadTag(groupedTags, "DamageType"),
            DamageResistances = ReadTag(groupedTags, "DamageResistance"),
            DamageImmunities = ReadTag(groupedTags, "DamageImmunity"),
            DamageVulnerabilities = ReadTag(groupedTags, "DamageVulnerability"),
            ConditionsInflicted = ReadTag(groupedTags, "ConditionInflicted"),
            ConditionImmunities = ReadTag(groupedTags, "ConditionImmunity"),
            SpellAttackTypes = ReadTag(groupedTags, "SpellAttackType"),
            AffectsCreatureTypes = ReadTag(groupedTags, "AffectsCreatureType"),
            MiscTags = ReadTag(groupedTags, "MiscTag"),
            AreaTags = ReadTag(groupedTags, "AreaTag"),
            Aliases = ReadTag(groupedTags, "Alias"),
            IsSrd = entity.IsSrd,
            IsBasicRules = entity.IsBasicRules,
            IsSrd2024 = entity.IsSrd2024,
            IsBasicRules2024 = entity.IsBasicRules2024,
            HasFluff = entity.HasFluff,
            HasFluffImages = entity.HasFluffImages,
            RawJson = entity.RawJson
        };
    }

    private static List<string> ReadTag(IReadOnlyDictionary<string, List<string>> tags, string category)
    {
        return tags.TryGetValue(category, out var values) ? values : [];
    }

    private static int GetMaxSpellLevel(int classLevel, string progressionCode)
    {
        return progressionCode switch
        {
            "full" => GetFullCasterMaxSpellLevel(classLevel),
            "1/2" => GetHalfCasterMaxSpellLevel(classLevel, roundUp: false),
            "artificer" => GetHalfCasterMaxSpellLevel(classLevel, roundUp: true),
            "1/3" => GetThirdCasterMaxSpellLevel(classLevel),
            "pact" => GetPactCasterMaxSpellLevel(classLevel),
            _ => 0
        };
    }

    private static int GetFullCasterMaxSpellLevel(int classLevel)
    {
        return classLevel switch
        {
            <= 0 => 0,
            1 => 1,
            2 => 1,
            3 => 2,
            4 => 2,
            5 => 3,
            6 => 3,
            7 => 4,
            8 => 4,
            9 => 5,
            10 => 5,
            11 => 6,
            12 => 6,
            13 => 7,
            14 => 7,
            15 => 8,
            16 => 8,
            _ => 9
        };
    }

    private static int GetHalfCasterMaxSpellLevel(int classLevel, bool roundUp)
    {
        var effectiveLevel = roundUp
            ? (int)Math.Ceiling(classLevel / 2.0)
            : classLevel / 2;

        return GetFullCasterMaxSpellLevel(effectiveLevel);
    }

    private static int GetThirdCasterMaxSpellLevel(int classLevel)
    {
        return GetFullCasterMaxSpellLevel(classLevel / 3);
    }

    private static int GetPactCasterMaxSpellLevel(int classLevel)
    {
        return classLevel switch
        {
            <= 0 => 0,
            <= 2 => 1,
            <= 4 => 2,
            <= 6 => 3,
            <= 8 => 4,
            _ => 5
        };
    }
}
