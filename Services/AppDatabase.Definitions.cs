using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<ClassDefinition>> GetClassDefinitionsAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<ClassDefinitionEntity>().OrderBy(classDefinition => classDefinition.Name).ToListAsync();
        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<RaceDefinition>> GetRaceDefinitionsAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<RaceDefinitionEntity>().OrderBy(definition => definition.Name).ToListAsync();
        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<BackgroundDefinition>> GetBackgroundDefinitionsAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<BackgroundDefinitionEntity>().OrderBy(definition => definition.Name).ToListAsync();
        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SubraceDefinition>> GetSubraceDefinitionsAsync(int raceDefinitionId)
    {
        await InitializeAsync();

        var raceDefinition = await _database.Table<RaceDefinitionEntity>()
            .Where(definition => definition.Id == raceDefinitionId)
            .FirstOrDefaultAsync();
        if (raceDefinition is null)
        {
            return [];
        }

        var entities = await _database.Table<SubraceDefinitionEntity>()
            .Where(definition => definition.RaceName == raceDefinition.Name && definition.RaceSource == raceDefinition.Source)
            .OrderBy(definition => definition.Name)
            .ToListAsync();

        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<FeatDefinition>> GetFeatDefinitionsAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<FeatDefinitionEntity>().OrderBy(definition => definition.Name).ToListAsync();
        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SubclassDefinition>> GetSubclassDefinitionsAsync(int classDefinitionId)
    {
        await InitializeAsync();

        var classDefinition = await _database.Table<ClassDefinitionEntity>()
            .Where(definition => definition.Id == classDefinitionId)
            .FirstOrDefaultAsync();
        if (classDefinition is null)
        {
            return [];
        }

        var classDefinitionIds = (await _database.Table<ClassDefinitionEntity>()
                .Where(definition => definition.Name == classDefinition.Name)
                .ToListAsync())
            .Select(definition => definition.Id)
            .ToHashSet();

        var entities = await _database.Table<SubclassDefinitionEntity>()
            .OrderBy(subclassDefinition => subclassDefinition.Name)
            .ToListAsync();

        return entities
            .Where(subclassDefinition => classDefinitionIds.Contains(subclassDefinition.ClassDefinitionId))
            .Select(ToModel)
            .ToList();
    }

    public async Task<CharacterOptionEffects> GetCharacterOptionEffectsAsync(Character character)
    {
        await InitializeAsync();

        var effects = new CharacterOptionEffects();

        if (character.RaceDefinitionId is not null)
        {
            var race = await _database.Table<RaceDefinitionEntity>()
                .Where(row => row.Id == character.RaceDefinitionId.Value)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, race?.RawJson, "Race");
            AddSelectedAbilityChoiceEffects(effects, race?.RawJson, $"Race:{race?.Id ?? 0}", race is null ? "Race" : $"Race: {race.Name}", character.RaceChoicesJson);
            await AddGrantedFeatEffectsAsync(effects, ExtractFeatsJson(race?.RawJson), character.RaceChoicesJson, race?.Name ?? "Race");
        }

        if (character.SubraceDefinitionId is not null)
        {
            var subrace = await _database.Table<SubraceDefinitionEntity>()
                .Where(row => row.Id == character.SubraceDefinitionId.Value)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, subrace?.RawJson, "Race version");
            AddSelectedAbilityChoiceEffects(effects, subrace?.RawJson, $"Subrace:{subrace?.Id ?? 0}", subrace is null ? "Race version" : $"Race version: {subrace.Name}", character.RaceChoicesJson);
            await AddGrantedFeatEffectsAsync(effects, ExtractFeatsJson(subrace?.RawJson), character.RaceChoicesJson, subrace?.Name ?? "Race version");
        }

        if (character.BackgroundDefinitionId is not null)
        {
            var background = await _database.Table<BackgroundDefinitionEntity>()
                .Where(row => row.Id == character.BackgroundDefinitionId.Value)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, background?.RawJson, string.IsNullOrWhiteSpace(background?.Name) ? "Background" : $"Background {background.Name}");
            AddSelectedAbilityChoiceEffects(effects, background?.RawJson, $"Background:{background?.Id ?? 0}", background is null ? "Background" : $"Background: {background.Name}", character.BackgroundChoicesJson);
            await AddGrantedFeatEffectsAsync(effects, background?.FeatsJson, character.BackgroundChoicesJson, background?.Name ?? "Background");
        }

        var featOccurrences = new Dictionary<int, int>();
        foreach (var feat in character.Feats.Where(feat => feat.FeatDefinitionId > 0))
        {
            var featDefinition = await _database.Table<FeatDefinitionEntity>()
                .Where(row => row.Id == feat.FeatDefinitionId)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, featDefinition?.RawJson, string.IsNullOrWhiteSpace(featDefinition?.Name) ? "Feat" : $"Feat {featDefinition.Name}");
            var occurrence = featOccurrences.GetValueOrDefault(feat.FeatDefinitionId);
            featOccurrences[feat.FeatDefinitionId] = occurrence + 1;
            var sourceKey = occurrence == 0
                ? $"Feat:{featDefinition?.Id ?? 0}"
                : $"Feat:{featDefinition?.Id ?? 0}:repeat:{occurrence}";
            AddSelectedAbilityChoiceEffects(effects, featDefinition?.RawJson, sourceKey, featDefinition is null ? "Feat" : $"Feat: {featDefinition.Name}", character.FeatChoicesJson);
        }

        var primaryClassDefinitionId = character.PrimaryClassDefinitionId
            ?? character.Classes.FirstOrDefault(characterClass => characterClass.ClassDefinitionId > 0)?.ClassDefinitionId;
        foreach (var characterClass in character.Classes.Where(characterClass =>
                     characterClass.ClassDefinitionId > 0
                     && characterClass.ClassDefinitionId == primaryClassDefinitionId))
        {
            await AddClassOptionEffectsAsync(effects, characterClass.ClassDefinitionId);
        }

        return effects;
    }

    private async Task AddClassOptionEffectsAsync(CharacterOptionEffects effects, int classDefinitionId)
    {
        var classDefinition = await _database.Table<ClassDefinitionEntity>()
            .Where(row => row.Id == classDefinitionId)
            .FirstOrDefaultAsync();
        if (classDefinition is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(classDefinition.RawJson))
        {
            return;
        }

        using var classDocument = JsonDocument.Parse(classDefinition.RawJson);
        var classElement = classDocument.RootElement;
        if (classElement.TryGetProperty("proficiency", out var savingThrows) && savingThrows.ValueKind == JsonValueKind.Array)
        {
            foreach (var savingThrow in savingThrows.EnumerateArray())
            {
                if (savingThrow.ValueKind == JsonValueKind.String)
                {
                    effects.SavingThrowProficiencies.Add(savingThrow.GetString() ?? "");
                }
            }
        }

        if (classElement.TryGetProperty("startingProficiencies", out var startingProficiencies)
            && startingProficiencies.TryGetProperty("skills", out var skills))
        {
            AddSkillProficiencies(effects, skills, "Class");
        }
    }

    private async Task AddGrantedFeatEffectsAsync(CharacterOptionEffects effects, string? featsJson, string choicesJson, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(featsJson) || !ShouldApplyGrantedFeats(choicesJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(featsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var featDefinitions = await _database.Table<FeatDefinitionEntity>().ToListAsync();
        foreach (var featReference in ReadGrantedFeatReferences(document.RootElement))
        {
            var featDefinition = featDefinitions.FirstOrDefault(definition =>
                string.Equals(definition.Name, featReference.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Source, featReference.Source, StringComparison.OrdinalIgnoreCase));
            if (featDefinition is null)
            {
                continue;
            }

            AddChoiceHint(
                effects,
                "Feats",
                sourceName,
                1,
                $"Grants {featDefinition.Name} ({featDefinition.Source}).");
            AddOptionEffects(effects, featDefinition.RawJson, $"Feat {featDefinition.Name}");
        }
    }

    private static bool ShouldApplyGrantedFeats(string choicesJson)
    {
        if (string.IsNullOrWhiteSpace(choicesJson))
        {
            return true;
        }

        using var document = JsonDocument.Parse(choicesJson);
        return !document.RootElement.TryGetProperty("applyGrantedFeats", out var applyGrantedFeats)
            || applyGrantedFeats.ValueKind != JsonValueKind.False;
    }

    private static string ExtractFeatsJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "";
        }

        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.TryGetProperty("feats", out var feats)
            ? feats.GetRawText()
            : "";
    }

    private static IEnumerable<(string Name, string Source)> ReadGrantedFeatReferences(JsonElement featsElement)
    {
        foreach (var entry in featsElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in entry.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                var parts = property.Name.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                yield return (parts[0], parts.Length > 1 ? parts[1].ToUpperInvariant() : "");
            }
        }
    }

    private async Task EnsureClassDataImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("ClassImportVersion");
        var count = await _database.Table<ClassDefinitionEntity>().CountAsync();

        if (import?.Value == ClassImportVersion && count > 0)
        {
            return;
        }

#if !SEED_BUILDER
        if (_useSeedDatabase)
        {
            throw BuildReferenceDataVersionMismatchException("class", "ClassImportVersion", import?.Value, ClassImportVersion);
        }
#endif

        await _database.DeleteAllAsync<ClassSpellAccessRuleEntity>();
        await _database.DeleteAllAsync<SubclassSpellAccessRuleEntity>();
        await _database.DeleteAllAsync<SubclassDefinitionEntity>();
        await _database.DeleteAllAsync<ClassDefinitionEntity>();
        await _database.DeleteAllAsync<SpellcastingProgressionEntity>();

        await SeedSpellcastingProgressionsAsync();
        var progressions = await _database.Table<SpellcastingProgressionEntity>().ToListAsync();

        await using var stream = await OpenAssetAsync("class/index.json");
        using var document = await JsonDocument.ParseAsync(stream);

        foreach (var fileProperty in document.RootElement.EnumerateObject())
        {
            var fileName = fileProperty.Value.GetString();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            await ImportClassFileAsync(fileName, progressions);
        }

        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = "ClassImportVersion", Value = ClassImportVersion });
    }

    private async Task SeedSpellcastingProgressionsAsync()
    {
        var progressions = new[]
        {
            new SpellcastingProgressionEntity { Code = "none", Name = "None", MulticlassWeight = 0, RoundingRule = "None", UsesPactMagic = false },
            new SpellcastingProgressionEntity { Code = "full", Name = "Full", MulticlassWeight = 1, RoundingRule = "Down", UsesPactMagic = false },
            new SpellcastingProgressionEntity { Code = "1/2", Name = "Half", MulticlassWeight = 0.5, RoundingRule = "Down", UsesPactMagic = false },
            new SpellcastingProgressionEntity { Code = "artificer", Name = "Half Round Up", MulticlassWeight = 0.5, RoundingRule = "Up", UsesPactMagic = false },
            new SpellcastingProgressionEntity { Code = "1/3", Name = "Third", MulticlassWeight = 1.0 / 3.0, RoundingRule = "Down", UsesPactMagic = false },
            new SpellcastingProgressionEntity { Code = "pact", Name = "Pact", MulticlassWeight = 0, RoundingRule = "None", UsesPactMagic = true }
        };

        await _database.InsertAllAsync(progressions);
    }

    private async Task ImportClassFileAsync(string fileName, IReadOnlyList<SpellcastingProgressionEntity> progressions)
    {
        await using var stream = await OpenAssetAsync($"class/{fileName}");
        using var document = await JsonDocument.ParseAsync(stream);

        var classDefinitionsByNaturalKey = new Dictionary<string, ClassDefinitionEntity>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.TryGetProperty("class", out var classes))
        {
            foreach (var classElement in classes.EnumerateArray())
            {
                var entity = new ClassDefinitionEntity
                {
                    Name = ReadString(classElement, "name"),
                    Source = ReadString(classElement, "source"),
                    Slug = BuildSlug(ReadString(classElement, "name"), ReadString(classElement, "source")),
                    SpellcastingProgressionId = FindProgressionId(progressions, ReadString(classElement, "casterProgression")),
                    RawJson = BuildClassRawJson(document.RootElement, classElement)
                };

                await _database.InsertAsync(entity);
                classDefinitionsByNaturalKey[$"{entity.Name}|{entity.Source}"] = entity;
            }
        }

        if (!document.RootElement.TryGetProperty("subclass", out var subclasses))
        {
            return;
        }

        var importedSubclassSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subclassElement in subclasses.EnumerateArray())
        {
            var className = ReadString(subclassElement, "className");
            var classSource = ReadString(subclassElement, "classSource");
            if (!classDefinitionsByNaturalKey.TryGetValue($"{className}|{classSource}", out var classDefinition))
            {
                continue;
            }

            var subclassSlug = BuildSubclassSlug(classDefinition.Slug, ReadString(subclassElement, "name"), ReadString(subclassElement, "source"));
            if (!importedSubclassSlugs.Add(subclassSlug))
            {
                continue;
            }

            await _database.InsertAsync(new SubclassDefinitionEntity
            {
                ClassDefinitionId = classDefinition.Id,
                Name = ReadString(subclassElement, "name"),
                Source = ReadString(subclassElement, "source"),
                Slug = subclassSlug,
                SpellcastingProgressionId = FindProgressionId(progressions, ReadString(subclassElement, "casterProgression")),
                RawJson = BuildSubclassRawJson(document.RootElement, subclassElement)
            });
        }
    }

    private static string BuildClassRawJson(JsonElement root, JsonElement classElement)
    {
        var enriched = JsonNode.Parse(classElement.GetRawText())?.AsObject();
        if (enriched is null)
        {
            return classElement.GetRawText();
        }

        var className = ReadString(classElement, "name");
        var classSource = ReadString(classElement, "source");
        var featureDetails = new JsonArray();

        if (root.TryGetProperty("classFeature", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                if (string.Equals(ReadString(feature, "className"), className, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ReadString(feature, "classSource"), classSource, StringComparison.OrdinalIgnoreCase))
                {
                    featureDetails.Add(JsonNode.Parse(feature.GetRawText()));
                }
            }
        }

        enriched["_featureDetails"] = featureDetails;
        return enriched.ToJsonString();
    }

    private static string BuildSubclassRawJson(JsonElement root, JsonElement subclassElement)
    {
        var enriched = JsonNode.Parse(subclassElement.GetRawText())?.AsObject();
        if (enriched is null)
        {
            return subclassElement.GetRawText();
        }

        var className = ReadString(subclassElement, "className");
        var classSource = ReadString(subclassElement, "classSource");
        var subclassName = ReadString(subclassElement, "shortName");
        if (string.IsNullOrWhiteSpace(subclassName))
        {
            subclassName = ReadString(subclassElement, "name");
        }

        var subclassSource = ReadString(subclassElement, "source");
        var featureDetails = new JsonArray();

        if (root.TryGetProperty("subclassFeature", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                if (string.Equals(ReadString(feature, "className"), className, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ReadString(feature, "classSource"), classSource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ReadString(feature, "subclassShortName"), subclassName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ReadString(feature, "subclassSource"), subclassSource, StringComparison.OrdinalIgnoreCase))
                {
                    featureDetails.Add(JsonNode.Parse(feature.GetRawText()));
                }
            }
        }

        enriched["_featureDetails"] = featureDetails;
        return enriched.ToJsonString();
    }

    private async Task EnsureCharacterOptionsImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("CharacterOptionImportVersion");
        var raceCount = await _database.Table<RaceDefinitionEntity>().CountAsync();
        var backgroundCount = await _database.Table<BackgroundDefinitionEntity>().CountAsync();
        var featCount = await _database.Table<FeatDefinitionEntity>().CountAsync();

        if (import?.Value == CharacterOptionImportVersion
            && raceCount > 0
            && backgroundCount > 0
            && featCount > 0)
        {
            return;
        }

#if !SEED_BUILDER
        if (_useSeedDatabase)
        {
            throw BuildReferenceDataVersionMismatchException(
                "character option",
                "CharacterOptionImportVersion",
                import?.Value,
                CharacterOptionImportVersion);
        }
#endif

        await _database.DeleteAllAsync<RaceDefinitionEntity>();
        await _database.DeleteAllAsync<SubraceDefinitionEntity>();
        await _database.DeleteAllAsync<BackgroundDefinitionEntity>();
        await _database.DeleteAllAsync<FeatDefinitionEntity>();

        await ImportRaceDefinitionsAsync();
        await ImportSubraceDefinitionsAsync();
        await ImportRaceVersionDefinitionsAsync();
        await ImportBackgroundDefinitionsAsync();
        await ImportFeatDefinitionsAsync();

        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = "CharacterOptionImportVersion", Value = CharacterOptionImportVersion });
    }

    private async Task ImportRaceDefinitionsAsync()
    {
        await using var stream = await OpenAssetAsync("races.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("race", out var races))
        {
            return;
        }

        foreach (var raceElement in races.EnumerateArray())
        {
            await _database.InsertAsync(new RaceDefinitionEntity
            {
                Name = ReadString(raceElement, "name"),
                Source = ReadString(raceElement, "source"),
                Slug = BuildSlug(ReadString(raceElement, "name"), ReadString(raceElement, "source")),
                Page = ReadInt(raceElement, "page"),
                SizeJson = ReadRawJson(raceElement, "size"),
                SpeedJson = ReadRawJson(raceElement, "speed"),
                AbilityJson = ReadRawJson(raceElement, "ability"),
                LanguageProficienciesJson = ReadRawJson(raceElement, "languageProficiencies"),
                TraitTagsJson = ReadRawJson(raceElement, "traitTags"),
                RawJson = raceElement.GetRawText()
            });
        }
    }

    private async Task ImportSubraceDefinitionsAsync()
    {
        await using var stream = await OpenAssetAsync("races.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("subrace", out var subraces))
        {
            return;
        }

        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        foreach (var subraceElement in subraces.EnumerateArray())
        {
            var copyElement = subraceElement.TryGetProperty("_copy", out var copy) ? copy : default;
            var raceName = ReadString(subraceElement, "raceName");
            var raceSource = ReadString(subraceElement, "raceSource");
            if (string.IsNullOrWhiteSpace(raceName) && copyElement.ValueKind == JsonValueKind.Object)
            {
                raceName = ReadString(copyElement, "name");
                raceSource = ReadString(copyElement, "source");
            }

            if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(raceSource))
            {
                continue;
            }

            var name = ReadString(subraceElement, "name");
            var source = ReadString(subraceElement, "source");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var raceDefinition = raceDefinitions.FirstOrDefault(definition =>
                string.Equals(definition.Name, raceName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Source, raceSource, StringComparison.OrdinalIgnoreCase));

            await _database.InsertAsync(new SubraceDefinitionEntity
            {
                RaceDefinitionId = raceDefinition?.Id,
                RaceName = raceName,
                RaceSource = raceSource,
                Name = name,
                Source = source,
                Slug = BuildSubraceSlug(raceName, raceSource, name, source),
                Page = ReadInt(subraceElement, "page"),
                RawJson = subraceElement.GetRawText()
            });
        }
    }

    private async Task ImportBackgroundDefinitionsAsync()
    {
        await using var stream = await OpenAssetAsync("backgrounds.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("background", out var backgrounds))
        {
            return;
        }

        foreach (var backgroundElement in backgrounds.EnumerateArray())
        {
            await _database.InsertAsync(new BackgroundDefinitionEntity
            {
                Name = ReadString(backgroundElement, "name"),
                Source = ReadString(backgroundElement, "source"),
                Slug = BuildSlug(ReadString(backgroundElement, "name"), ReadString(backgroundElement, "source")),
                Page = ReadInt(backgroundElement, "page"),
                AbilityJson = ReadRawJson(backgroundElement, "ability"),
                FeatsJson = ReadRawJson(backgroundElement, "feats"),
                SkillProficienciesJson = ReadRawJson(backgroundElement, "skillProficiencies"),
                ToolProficienciesJson = ReadRawJson(backgroundElement, "toolProficiencies"),
                RawJson = backgroundElement.GetRawText()
            });
        }
    }

    private async Task ImportFeatDefinitionsAsync()
    {
        await using var stream = await OpenAssetAsync("feats.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("feat", out var feats))
        {
            return;
        }

        foreach (var featElement in feats.EnumerateArray())
        {
            var structuredAbilityJson = ReadRawJson(featElement, "ability");
            var inferredAbilityJson = string.IsNullOrWhiteSpace(structuredAbilityJson)
                ? AbilityRuleParser.BuildAbilityJsonFromText(featElement)
                : "";
            var abilityJson = string.IsNullOrWhiteSpace(structuredAbilityJson) ? inferredAbilityJson : structuredAbilityJson;
            var rawJson = string.IsNullOrWhiteSpace(inferredAbilityJson)
                ? featElement.GetRawText()
                : AbilityRuleParser.EnrichRawJsonWithAbility(featElement, inferredAbilityJson);

            await _database.InsertAsync(new FeatDefinitionEntity
            {
                Name = ReadString(featElement, "name"),
                Source = ReadString(featElement, "source"),
                Slug = BuildSlug(ReadString(featElement, "name"), ReadString(featElement, "source")),
                Page = ReadInt(featElement, "page"),
                Category = ReadString(featElement, "category"),
                PrerequisiteJson = ReadRawJson(featElement, "prerequisite"),
                AdditionalSpellsJson = ReadRawJson(featElement, "additionalSpells"),
                AbilityJson = abilityJson,
                IsRepeatable = ReadBool(featElement, "repeatable"),
                RawJson = rawJson
            });
        }
    }

    private static RaceDefinition ToModel(RaceDefinitionEntity entity)
    {
        return new RaceDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            Slug = entity.Slug,
            SizeJson = entity.SizeJson,
            SpeedJson = entity.SpeedJson,
            AbilityJson = entity.AbilityJson,
            LanguageProficienciesJson = entity.LanguageProficienciesJson,
            TraitTagsJson = entity.TraitTagsJson,
            RawJson = entity.RawJson
        };
    }

    private static SubraceDefinition ToModel(SubraceDefinitionEntity entity)
    {
        return new SubraceDefinition
        {
            Id = entity.Id,
            RaceDefinitionId = entity.RaceDefinitionId,
            RaceName = entity.RaceName,
            RaceSource = entity.RaceSource,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            Slug = entity.Slug,
            RawJson = entity.RawJson
        };
    }

    private static BackgroundDefinition ToModel(BackgroundDefinitionEntity entity)
    {
        return new BackgroundDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            Slug = entity.Slug,
            AbilityJson = entity.AbilityJson,
            FeatsJson = entity.FeatsJson,
            SkillProficienciesJson = entity.SkillProficienciesJson,
            ToolProficienciesJson = entity.ToolProficienciesJson,
            RawJson = entity.RawJson
        };
    }

    private static FeatDefinition ToModel(FeatDefinitionEntity entity)
    {
        return new FeatDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            Category = entity.Category,
            Slug = entity.Slug,
            PrerequisiteJson = entity.PrerequisiteJson,
            AdditionalSpellsJson = entity.AdditionalSpellsJson,
            AbilityJson = entity.AbilityJson,
            IsRepeatable = entity.IsRepeatable,
            RawJson = entity.RawJson
        };
    }

    private static ClassDefinition ToModel(ClassDefinitionEntity entity)
    {
        return new ClassDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Slug = entity.Slug,
            SpellcastingProgressionId = entity.SpellcastingProgressionId,
            RawJson = entity.RawJson
        };
    }

    private static SubclassDefinition ToModel(SubclassDefinitionEntity entity)
    {
        return new SubclassDefinition
        {
            Id = entity.Id,
            ClassDefinitionId = entity.ClassDefinitionId,
            Name = entity.Name,
            Source = entity.Source,
            Slug = entity.Slug,
            RawJson = entity.RawJson
        };
    }

    private static int? FindProgressionId(IEnumerable<SpellcastingProgressionEntity> progressions, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "none";
        }

        return progressions.FirstOrDefault(progression => string.Equals(progression.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string BuildSlug(string name, string source)
    {
        return $"{NormalizeSlugPart(name)}|{NormalizeSlugPart(source)}";
    }

    private static string BuildSubclassSlug(string classSlug, string name, string source)
    {
        return $"{classSlug}|{NormalizeSlugPart(name)}|{NormalizeSlugPart(source)}";
    }

    private static string BuildSubraceSlug(string raceName, string raceSource, string name, string source)
    {
        return $"{NormalizeSlugPart(raceName)}|{NormalizeSlugPart(raceSource)}|{NormalizeSlugPart(name)}|{NormalizeSlugPart(source)}";
    }

    private static string ExtractRaceVersionName(string raceName, string versionName)
    {
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return "";
        }

        var prefix = $"{raceName};";
        return versionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? versionName[prefix.Length..].Trim()
            : "";
    }

    private static string BuildRaceVersionRawJson(
        JsonElement raceElement,
        JsonElement versionElement,
        string raceName,
        string raceSource,
        string subraceName,
        string versionSource)
    {
        var raw = new JsonObject
        {
            ["name"] = subraceName,
            ["source"] = versionSource,
            ["raceName"] = raceName,
            ["raceSource"] = raceSource
        };

        var page = ReadInt(raceElement, "page");
        if (page is not null)
        {
            raw["page"] = page.Value;
        }

        if (TryBuildRaceVersionEntries(versionElement, out var entries))
        {
            raw["entries"] = entries;
        }

        return raw.ToJsonString();
    }

    private static bool TryBuildRaceVersionEntries(JsonElement versionElement, out JsonArray entries)
    {
        entries = [];
        if (!versionElement.TryGetProperty("_mod", out var mod)
            || mod.ValueKind != JsonValueKind.Object
            || !mod.TryGetProperty("entries", out var entriesMod))
        {
            return false;
        }

        foreach (var replacement in EnumerateEntryReplacements(entriesMod))
        {
            if (replacement.TryGetProperty("items", out var items))
            {
                entries.Add(JsonNode.Parse(items.GetRawText()));
            }
        }

        return entries.Count > 0;
    }

    private static IEnumerable<JsonElement> EnumerateEntryReplacements(JsonElement entriesMod)
    {
        if (entriesMod.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entriesMod.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (entriesMod.ValueKind == JsonValueKind.Object)
        {
            yield return entriesMod;
        }
    }

    private static string NormalizeSlugPart(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "-");
    }

    private static void AddOptionEffects(CharacterOptionEffects effects, string? rawJson, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.TryGetProperty("ability", out var ability))
        {
            AddAbilityBonuses(effects, ability, sourceLabel);
        }

        if (document.RootElement.TryGetProperty("skillProficiencies", out var skillProficiencies))
        {
            AddSkillProficiencies(effects, skillProficiencies, sourceLabel);
        }

        if (document.RootElement.TryGetProperty("toolProficiencies", out var toolProficiencies))
        {
            AddNamedOrChoiceProficiencies(effects, toolProficiencies, sourceLabel, "Tools");
        }

        if (document.RootElement.TryGetProperty("languageProficiencies", out var languageProficiencies))
        {
            AddNamedOrChoiceProficiencies(effects, languageProficiencies, sourceLabel, "Languages");
        }

        if (document.RootElement.TryGetProperty("skillToolLanguageProficiencies", out var skillToolLanguageProficiencies))
        {
            AddSkillToolLanguageChoices(effects, skillToolLanguageProficiencies, sourceLabel);
        }
    }

    private static void AddAbilityBonuses(CharacterOptionEffects effects, JsonElement abilityElement, string sourceLabel)
    {
        if (abilityElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in abilityElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in entry.EnumerateObject())
            {
                if (IsAbilityCode(property.Name) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var bonus))
                {
                    effects.AbilityBonuses[property.Name] = effects.AbilityBonuses.GetValueOrDefault(property.Name) + bonus;
                    continue;
                }

                // Ability choices are represented as explicit selectable requirements in the character UI.
            }
        }
    }

    private async Task ImportRaceVersionDefinitionsAsync()
    {
        await using var stream = await OpenAssetAsync("races.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("race", out var races))
        {
            return;
        }

        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var existingSubraces = await _database.Table<SubraceDefinitionEntity>().ToListAsync();

        foreach (var raceElement in races.EnumerateArray())
        {
            if (!raceElement.TryGetProperty("_versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var raceName = ReadString(raceElement, "name");
            var raceSource = ReadString(raceElement, "source");
            if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(raceSource))
            {
                continue;
            }

            var raceDefinition = raceDefinitions.FirstOrDefault(definition =>
                string.Equals(definition.Name, raceName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.Source, raceSource, StringComparison.OrdinalIgnoreCase));

            foreach (var versionElement in versions.EnumerateArray())
            {
                var versionName = ReadString(versionElement, "name");
                var versionSource = ReadString(versionElement, "source");
                var subraceName = ExtractRaceVersionName(raceName, versionName);
                if (string.IsNullOrWhiteSpace(subraceName) || string.IsNullOrWhiteSpace(versionSource))
                {
                    continue;
                }

                if (existingSubraces.Any(subrace =>
                    string.Equals(subrace.RaceName, raceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(subrace.RaceSource, raceSource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(subrace.Name, subraceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(subrace.Source, versionSource, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var rawJson = BuildRaceVersionRawJson(raceElement, versionElement, raceName, raceSource, subraceName, versionSource);
                await _database.InsertAsync(new SubraceDefinitionEntity
                {
                    RaceDefinitionId = raceDefinition?.Id,
                    RaceName = raceName,
                    RaceSource = raceSource,
                    Name = subraceName,
                    Source = versionSource,
                    Slug = BuildSubraceSlug(raceName, raceSource, subraceName, versionSource),
                    Page = ReadInt(raceElement, "page"),
                    RawJson = rawJson
                });
            }
        }
    }

    private static void AddSelectedAbilityChoiceEffects(
        CharacterOptionEffects effects,
        string? rawJson,
        string sourceKey,
        string sourceName,
        string choicesJson)
    {
        var requirements = CharacterAbilityChoiceService.BuildRequirements(rawJson, sourceKey, sourceName);
        if (requirements.Count == 0)
        {
            return;
        }

        var selectedChoices = CharacterAbilityChoiceService.ReadSelectedChoices(choicesJson);
        CharacterAbilityChoiceService.AddSelectedBonuses(effects.AbilityBonuses, requirements, selectedChoices);
    }

    private static void AddSkillProficiencies(CharacterOptionEffects effects, JsonElement skillElement, string sourceLabel)
    {
        if (skillElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in skillElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in entry.EnumerateObject())
            {
                if (property.NameEquals("choose"))
                {
                    AddChoiceHint(effects, "Skills", sourceLabel, ReadChoiceCount(property.Value), "Choose skill proficiencies.");
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    effects.SkillProficiencies.Add(property.Name);
                }
            }
        }
    }

    private static void AddSkillToolLanguageChoices(CharacterOptionEffects effects, JsonElement proficiencyElement, string sourceLabel)
    {
        if (proficiencyElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in proficiencyElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("choose", out var choose))
            {
                continue;
            }

            foreach (var choice in EnumerateChoiceEntries(choose))
            {
                var count = ReadChoiceCount(choice);
                var fromValues = ReadChoiceFromValues(choice).ToList();
                var hasSkill = fromValues.Any(value => value.Contains("Skill", StringComparison.OrdinalIgnoreCase));
                var hasTool = fromValues.Any(value => value.Contains("Tool", StringComparison.OrdinalIgnoreCase));
                var hasLanguage = fromValues.Any(value => value.Contains("Language", StringComparison.OrdinalIgnoreCase));
                if (hasSkill && hasTool)
                {
                    AddChoiceHint(effects, "SkillTools", sourceLabel, count, count == 1
                        ? "Choose 1 skill or tool proficiency."
                        : $"Choose {count} skill or tool proficiencies.");
                    continue;
                }

                if (hasSkill)
                {
                    AddChoiceHint(effects, "Skills", sourceLabel, count, count == 1
                        ? "Choose 1 skill/tool proficiency."
                        : $"Choose {count} skill/tool proficiencies.");
                }

                if (hasTool)
                {
                    AddChoiceHint(effects, "Tools", sourceLabel, count, count == 1
                        ? "Choose 1 tool proficiency."
                        : $"Choose {count} tool proficiencies.");
                }

                if (hasLanguage)
                {
                    AddChoiceHint(effects, "Languages", sourceLabel, count, count == 1
                        ? "Choose 1 language."
                        : $"Choose {count} languages.");
                }
            }
        }
    }

    private static void AddNamedOrChoiceProficiencies(CharacterOptionEffects effects, JsonElement proficiencyElement, string sourceLabel, string category)
    {
        if (proficiencyElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in proficiencyElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in entry.EnumerateObject())
            {
                if (property.NameEquals("choose"))
                {
                    AddChoiceHint(effects, category, sourceLabel, ReadChoiceCount(property.Value), category == "Tools" ? "Choose tool proficiencies." : "Choose languages.");
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    AddAutomaticProficiency(effects, category, property.Name);
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count))
                {
                    AddChoiceHint(effects, category, sourceLabel, count, FormatGenericChoiceDescription(category, property.Name, count));
                }
            }
        }
    }

    private static void AddAutomaticProficiency(CharacterOptionEffects effects, string category, string name)
    {
        if (category == "Tools")
        {
            effects.ToolProficiencies.Add(FormatProficiencyName(name));
            return;
        }

        if (category == "Languages")
        {
            effects.LanguageProficiencies.Add(FormatProficiencyName(name));
        }
    }

    private static string FormatGenericChoiceDescription(string category, string source, int count)
    {
        var formatted = FormatProficiencyName(source);
        return category == "Tools"
            ? $"Choose {count} {formatted} option{(count == 1 ? "" : "s")}."
            : $"Choose {count} language{(count == 1 ? "" : "s")}.";
    }

    private static string FormatProficiencyName(string value)
    {
        return value switch
        {
            "anyGamingSet" => "Gaming Set",
            "anyStandard" => "Standard Language",
            "anyExotic" => "Exotic Language",
            "anyTool" => "Tool",
            "anyLanguage" => "Language",
            _ => value
        };
    }

    private static IEnumerable<JsonElement> EnumerateChoiceEntries(JsonElement choose)
    {
        if (choose.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choose.EnumerateArray())
            {
                yield return choice;
            }

            yield break;
        }

        if (choose.ValueKind == JsonValueKind.Object)
        {
            yield return choose;
        }
    }

    private static IEnumerable<string> ReadChoiceFromValues(JsonElement choice)
    {
        if (!choice.TryGetProperty("from", out var from))
        {
            yield break;
        }

        if (from.ValueKind == JsonValueKind.String)
        {
            yield return from.GetString() ?? "";
            yield break;
        }

        if (from.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in from.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return value.GetString() ?? "";
            }
        }
    }

    private static int ReadChoiceCount(JsonElement choice)
    {
        if (choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("count", out var count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 1;
    }

    private static void AddChoiceHint(CharacterOptionEffects effects, string category, string sourceLabel, int count, string description)
    {
        var source = string.IsNullOrWhiteSpace(sourceLabel) ? "Option" : sourceLabel;
        effects.ChoiceHints.Add(new CharacterOptionChoice(category, source, Math.Max(1, count), description));
        effects.DeferredChoices.Add($"{source}: {description}");
    }

    private static bool IsAbilityCode(string value)
    {
        return value is "str" or "dex" or "con" or "int" or "wis" or "cha";
    }
}
