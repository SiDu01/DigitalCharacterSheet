using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using SQLite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalCharacterSheet.Services;

public sealed class SpellDatabase
{
    private const int DatabaseVersion = 1;
    private const int SchemaVersion = 1;
    private const string ImportVersion = "spells-v1";
    private const string ClassImportVersion = "classes-v5";
    private const string CharacterOptionImportVersion = "character-options-v1";
    private const string SpellAccessImportVersion = "class-spell-access-v2";
    private const string ItemImportVersion = "items-v1";
    private const string SeedDatabaseVersion = "seed-v1";
    private const string DatabaseFileName = "digital-character-sheet.db3";
    private const string SeedDatabaseAssetName = "seed/digital-character-sheet.db3";
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private readonly SQLiteAsyncConnection _database;
    private readonly SpellImportService _importService;
    private bool _initialized;

    public SpellDatabase(SpellImportService importService)
#if SEED_BUILDER
        : this(importService, Path.Combine("bin", DatabaseFileName))
#else
        : this(importService, Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName), useSeedDatabase: true)
#endif
    {
    }

    public SpellDatabase(SpellImportService importService, string databasePath, bool useSeedDatabase = false)
    {
        _importService = importService;
        if (useSeedDatabase)
        {
            TryCopySeedDatabase(databasePath);
        }

        _database = new SQLiteAsyncConnection(databasePath);
    }

    public static async Task CreateSeedDatabaseAsync(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var database = new SpellDatabase(new SpellImportService(), databasePath);
        await database.InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await InitializationLock.WaitAsync();

        try
        {
            if (_initialized)
            {
                return;
            }

            var initializationStep = "starting";

            try
            {
                await CreateTableAsync<DatabaseMetadata>("DatabaseMetadata");
                await CreateTableAsync<SpellEntity>("Spells");
                await CreateTableAsync<SpellTagEntity>("SpellTags");
                await CreateTableAsync<CharacterEntity>("Characters");
                await CreateTableAsync<CharacterSpellEntity>("CharacterSpells");
                await CreateTableAsync<SpellcastingProgressionEntity>("SpellcastingProgressions");
                await CreateTableAsync<ClassDefinitionEntity>("ClassDefinitions");
                await CreateTableAsync<SubclassDefinitionEntity>("SubclassDefinitions");
                await CreateTableAsync<CharacterClassEntity>("CharacterClasses");
                await CreateTableAsync<ClassSpellAccessRuleEntity>("ClassSpellAccessRules");
                await CreateTableAsync<SubclassSpellAccessRuleEntity>("SubclassSpellAccessRules");
                await CreateTableAsync<CharacterSpellSlotEntity>("CharacterSpellSlots");
                await CreateTableAsync<CharacterSavingThrowEntity>("CharacterSavingThrows");
                await CreateTableAsync<CharacterSkillEntity>("CharacterSkills");
                await CreateTableAsync<CharacterToolProficiencyEntity>("CharacterToolProficiencies");
                await CreateTableAsync<CharacterLanguageProficiencyEntity>("CharacterLanguageProficiencies");
                await CreateTableAsync<RaceDefinitionEntity>("RaceDefinitions");
                await CreateTableAsync<SubraceDefinitionEntity>("SubraceDefinitions");
                await CreateTableAsync<BackgroundDefinitionEntity>("BackgroundDefinitions");
                await CreateTableAsync<FeatDefinitionEntity>("FeatDefinitions");
                await CreateTableAsync<CharacterFeatEntity>("CharacterFeats");
                await CreateTableAsync<CharacterHiddenFeatureEntity>("CharacterHiddenFeatures");
                await CreateTableAsync<SourceSettingEntity>("SourceSettings");
                await CreateTableAsync<ItemDefinitionEntity>("ItemDefinitions");
                await CreateTableAsync<ItemWeaponStatEntity>("ItemWeaponStats");
                await CreateTableAsync<ItemArmorStatEntity>("ItemArmorStats");
                await CreateTableAsync<ItemPropertyEntity>("ItemProperties");
                await CreateTableAsync<ItemDefinitionPropertyEntity>("ItemDefinitionProperties");
                await CreateTableAsync<ItemTypeEntity>("ItemTypes");
                await CreateTableAsync<ItemBonusEntity>("ItemBonuses");
                await CreateTableAsync<ItemAttachedSpellEntity>("ItemAttachedSpells");
                await CreateTableAsync<ItemAttunementRequirementEntity>("ItemAttunementRequirements");
                await CreateTableAsync<ItemGroupEntity>("ItemGroups");
                await CreateTableAsync<ItemGroupMemberEntity>("ItemGroupMembers");
                await CreateTableAsync<MagicItemVariantEntity>("MagicItemVariants");
                await CreateTableAsync<ItemFluffEntity>("ItemFluff");

                initializationStep = "checking character columns";
                await EnsureCharacterColumnsAsync();
                initializationStep = "checking class columns";
                await EnsureClassColumnsAsync();
                initializationStep = "checking subclass columns";
                await EnsureSubclassColumnsAsync();
                initializationStep = "checking database version";
                await EnsureDatabaseVersionAsync();
                initializationStep = "importing spells";
                await EnsureImportedAsync();
                initializationStep = "importing classes";
                await EnsureClassDataImportedAsync();
                initializationStep = "importing character options";
                await EnsureCharacterOptionsImportedAsync();
                initializationStep = "importing spell access";
                await EnsureSpellAccessImportedAsync();
#if SEED_BUILDER
                initializationStep = "importing items";
                await EnsureItemDataImportedAsync();
#endif
                initializationStep = "checking source settings";
                await EnsureSourceSettingsAsync();
                initializationStep = "writing database metadata";
                await WriteDatabaseVersionMetadataAsync();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Database initialization failed while {initializationStep}.", exception);
            }

            _initialized = true;

            async Task CreateTableAsync<T>(string tableName) where T : new()
            {
                initializationStep = $"creating table {tableName}";
                await _database.CreateTableAsync<T>();
            }
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<Character>> GetCharactersAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<CharacterEntity>().OrderBy(character => character.Name).ToListAsync();
        var classes = await GetAllCharacterClassesAsync();
        var feats = await GetAllCharacterFeatsAsync();
        var savingThrows = await GetAllCharacterSavingThrowsAsync();
        var skills = await GetAllCharacterSkillsAsync();
        var toolProficiencies = await GetAllCharacterToolProficienciesAsync();
        var languageProficiencies = await GetAllCharacterLanguageProficienciesAsync();
        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var subraceDefinitions = await _database.Table<SubraceDefinitionEntity>().ToListAsync();
        var backgroundDefinitions = await _database.Table<BackgroundDefinitionEntity>().ToListAsync();
        return entities
            .Select(entity => ToModel(
                entity,
                classes.Where(characterClass => characterClass.CharacterId == entity.Id),
                feats.Where(feat => feat.CharacterId == entity.Id),
                savingThrows.Where(savingThrow => savingThrow.CharacterId == entity.Id),
                skills.Where(skill => skill.CharacterId == entity.Id),
                toolProficiencies.Where(tool => tool.CharacterId == entity.Id),
                languageProficiencies.Where(language => language.CharacterId == entity.Id),
                raceDefinitions,
                subraceDefinitions,
                backgroundDefinitions))
            .ToList();
    }

    public async Task<Character?> GetCharacterAsync(int id)
    {
        await InitializeAsync();

        var entity = await _database.Table<CharacterEntity>().Where(character => character.Id == id).FirstOrDefaultAsync();
        if (entity is null)
        {
            return null;
        }

        var classes = await GetCharacterClassesAsync(entity.Id);
        var feats = await GetCharacterFeatsAsync(entity.Id);
        var savingThrows = await GetCharacterSavingThrowsAsync(entity.Id);
        var skills = await GetCharacterSkillsAsync(entity.Id);
        var toolProficiencies = await GetCharacterToolProficienciesAsync(entity.Id);
        var languageProficiencies = await GetCharacterLanguageProficienciesAsync(entity.Id);
        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var subraceDefinitions = await _database.Table<SubraceDefinitionEntity>().ToListAsync();
        var backgroundDefinitions = await _database.Table<BackgroundDefinitionEntity>().ToListAsync();
        return ToModel(entity, classes, feats, savingThrows, skills, toolProficiencies, languageProficiencies, raceDefinitions, subraceDefinitions, backgroundDefinitions);
    }

    public async Task<Character> AddCharacterAsync(Character character)
    {
        await InitializeAsync();

        if (string.IsNullOrWhiteSpace(character.Name))
        {
            throw new ArgumentException("Character name is required.", nameof(character));
        }

        var now = DateTime.UtcNow;
        var entity = new CharacterEntity
        {
            Name = character.Name.Trim(),
            RaceDefinitionId = character.RaceDefinitionId,
            SubraceDefinitionId = character.SubraceDefinitionId,
            RaceName = character.RaceName.Trim(),
            RaceChoicesJson = character.RaceChoicesJson.Trim(),
            BackgroundDefinitionId = character.BackgroundDefinitionId,
            BackgroundName = character.BackgroundName.Trim(),
            BackgroundChoicesJson = character.BackgroundChoicesJson.Trim(),
            FeatChoicesJson = character.FeatChoicesJson.Trim(),
            ClassName = character.ClassName.Trim(),
            Level = character.Level,
            Strength = ClampAbility(character.Strength),
            Dexterity = ClampAbility(character.Dexterity),
            Constitution = ClampAbility(character.Constitution),
            Intelligence = ClampAbility(character.Intelligence),
            Wisdom = ClampAbility(character.Wisdom),
            Charisma = ClampAbility(character.Charisma),
            CreatedAt = now,
            UpdatedAt = now
        };

        await _database.InsertAsync(entity);

        foreach (var characterClass in character.Classes.Where(characterClass => characterClass.ClassDefinitionId > 0))
        {
            await _database.InsertAsync(new CharacterClassEntity
            {
                CharacterId = entity.Id,
                ClassDefinitionId = characterClass.ClassDefinitionId,
                SubclassDefinitionId = characterClass.SubclassDefinitionId,
                Level = Math.Clamp(characterClass.Level, 1, 20)
            });
        }

        foreach (var feat in character.Feats.Where(feat => feat.FeatDefinitionId > 0))
        {
            await _database.InsertAsync(new CharacterFeatEntity
            {
                CharacterId = entity.Id,
                FeatDefinitionId = feat.FeatDefinitionId
            });
        }

        await InsertCharacterSavingThrowsAsync(entity.Id, character.SavingThrows);
        await InsertCharacterSkillsAsync(entity.Id, character.Skills);
        await InsertCharacterToolProficienciesAsync(entity.Id, character.ToolProficiencies);
        await InsertCharacterLanguageProficienciesAsync(entity.Id, character.LanguageProficiencies);

        var classes = await GetCharacterClassesAsync(entity.Id);
        var feats = await GetCharacterFeatsAsync(entity.Id);
        var savingThrows = await GetCharacterSavingThrowsAsync(entity.Id);
        var skills = await GetCharacterSkillsAsync(entity.Id);
        var toolProficiencies = await GetCharacterToolProficienciesAsync(entity.Id);
        var languageProficiencies = await GetCharacterLanguageProficienciesAsync(entity.Id);
        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var subraceDefinitions = await _database.Table<SubraceDefinitionEntity>().ToListAsync();
        var backgroundDefinitions = await _database.Table<BackgroundDefinitionEntity>().ToListAsync();
        return ToModel(entity, classes, feats, savingThrows, skills, toolProficiencies, languageProficiencies, raceDefinitions, subraceDefinitions, backgroundDefinitions);
    }

    public async Task UpdateCharacterAsync(Character character)
    {
        await InitializeAsync();

        if (character.Id <= 0)
        {
            throw new ArgumentException("Character id is required.", nameof(character));
        }

        if (string.IsNullOrWhiteSpace(character.Name))
        {
            throw new ArgumentException("Character name is required.", nameof(character));
        }

        var entity = await _database.Table<CharacterEntity>()
            .Where(row => row.Id == character.Id)
            .FirstOrDefaultAsync();

        if (entity is null)
        {
            throw new InvalidOperationException("Character was not found.");
        }

        entity.Name = character.Name.Trim();
        entity.RaceDefinitionId = character.RaceDefinitionId;
        entity.SubraceDefinitionId = character.SubraceDefinitionId;
        entity.RaceName = character.RaceName.Trim();
        entity.RaceChoicesJson = character.RaceChoicesJson.Trim();
        entity.BackgroundDefinitionId = character.BackgroundDefinitionId;
        entity.BackgroundName = character.BackgroundName.Trim();
        entity.BackgroundChoicesJson = character.BackgroundChoicesJson.Trim();
        entity.FeatChoicesJson = character.FeatChoicesJson.Trim();
        entity.ClassName = character.ClassName.Trim();
        entity.Level = character.Level;
        entity.Strength = ClampAbility(character.Strength);
        entity.Dexterity = ClampAbility(character.Dexterity);
        entity.Constitution = ClampAbility(character.Constitution);
        entity.Intelligence = ClampAbility(character.Intelligence);
        entity.Wisdom = ClampAbility(character.Wisdom);
        entity.Charisma = ClampAbility(character.Charisma);
        entity.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(entity);

        var existingClasses = await _database.Table<CharacterClassEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingClass in existingClasses)
        {
            await _database.DeleteAsync(existingClass);
        }

        foreach (var characterClass in character.Classes.Where(characterClass => characterClass.ClassDefinitionId > 0))
        {
            await _database.InsertAsync(new CharacterClassEntity
            {
                CharacterId = character.Id,
                ClassDefinitionId = characterClass.ClassDefinitionId,
                SubclassDefinitionId = characterClass.SubclassDefinitionId,
                Level = Math.Clamp(characterClass.Level, 1, 20)
            });
        }

        var existingFeats = await _database.Table<CharacterFeatEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingFeat in existingFeats)
        {
            await _database.DeleteAsync(existingFeat);
        }

        foreach (var feat in character.Feats.Where(feat => feat.FeatDefinitionId > 0))
        {
            await _database.InsertAsync(new CharacterFeatEntity
            {
                CharacterId = character.Id,
                FeatDefinitionId = feat.FeatDefinitionId
            });
        }

        var existingSavingThrows = await _database.Table<CharacterSavingThrowEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingSavingThrow in existingSavingThrows)
        {
            await _database.DeleteAsync(existingSavingThrow);
        }

        await InsertCharacterSavingThrowsAsync(character.Id, character.SavingThrows);

        var existingSkills = await _database.Table<CharacterSkillEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingSkill in existingSkills)
        {
            await _database.DeleteAsync(existingSkill);
        }

        await InsertCharacterSkillsAsync(character.Id, character.Skills);

        var existingTools = await _database.Table<CharacterToolProficiencyEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingTool in existingTools)
        {
            await _database.DeleteAsync(existingTool);
        }

        await InsertCharacterToolProficienciesAsync(character.Id, character.ToolProficiencies);

        var existingLanguages = await _database.Table<CharacterLanguageProficiencyEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingLanguage in existingLanguages)
        {
            await _database.DeleteAsync(existingLanguage);
        }

        await InsertCharacterLanguageProficienciesAsync(character.Id, character.LanguageProficiencies);
    }

    public async Task DeleteCharacterAsync(int characterId)
    {
        await InitializeAsync();

        var spellLinks = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId)
            .ToListAsync();
        foreach (var spellLink in spellLinks)
        {
            await _database.DeleteAsync(spellLink);
        }

        var classRows = await _database.Table<CharacterClassEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var classRow in classRows)
        {
            await _database.DeleteAsync(classRow);
        }

        var featRows = await _database.Table<CharacterFeatEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var featRow in featRows)
        {
            await _database.DeleteAsync(featRow);
        }

        var hiddenFeatureRows = await _database.Table<CharacterHiddenFeatureEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var hiddenFeatureRow in hiddenFeatureRows)
        {
            await _database.DeleteAsync(hiddenFeatureRow);
        }

        var savingThrowRows = await _database.Table<CharacterSavingThrowEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var savingThrowRow in savingThrowRows)
        {
            await _database.DeleteAsync(savingThrowRow);
        }

        var skillRows = await _database.Table<CharacterSkillEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var skillRow in skillRows)
        {
            await _database.DeleteAsync(skillRow);
        }

        var toolRows = await _database.Table<CharacterToolProficiencyEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var toolRow in toolRows)
        {
            await _database.DeleteAsync(toolRow);
        }

        var languageRows = await _database.Table<CharacterLanguageProficiencyEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var languageRow in languageRows)
        {
            await _database.DeleteAsync(languageRow);
        }

        var character = await _database.Table<CharacterEntity>()
            .Where(entity => entity.Id == characterId)
            .FirstOrDefaultAsync();
        if (character is not null)
        {
            await _database.DeleteAsync(character);
        }

        var slotRows = await _database.Table<CharacterSpellSlotEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var slotRow in slotRows)
        {
            await _database.DeleteAsync(slotRow);
        }
    }

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

    public async Task<IReadOnlyList<CharacterClass>> GetCharacterClassesAsync(int characterId)
    {
        await InitializeAsync();

        var classRows = await _database.Table<CharacterClassEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var subclassDefinitions = await _database.Table<SubclassDefinitionEntity>().ToListAsync();
        return classRows.Select(row => ToModel(row, classDefinitions, subclassDefinitions)).ToList();
    }

    public async Task<IReadOnlyList<CharacterFeat>> GetCharacterFeatsAsync(int characterId)
    {
        await InitializeAsync();

        var featRows = await _database.Table<CharacterFeatEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        var featDefinitions = await _database.Table<FeatDefinitionEntity>().ToListAsync();
        return featRows.Select(row => ToModel(row, featDefinitions)).ToList();
    }

    public async Task<IReadOnlyList<CharacterSavingThrow>> GetCharacterSavingThrowsAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterSavingThrowEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return MergeSavingThrows(rows.Select(ToModel));
    }

    public async Task<IReadOnlyList<CharacterSkill>> GetCharacterSkillsAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterSkillEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return MergeSkills(rows.Select(ToModel));
    }

    public async Task<IReadOnlyList<CharacterToolProficiency>> GetCharacterToolProficienciesAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterToolProficiencyEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return MergeToolProficiencies(rows.Select(ToModel));
    }

    public async Task<IReadOnlyList<CharacterLanguageProficiency>> GetCharacterLanguageProficienciesAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterLanguageProficiencyEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return MergeLanguageProficiencies(rows.Select(ToModel));
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
        }

        if (character.SubraceDefinitionId is not null)
        {
            var subrace = await _database.Table<SubraceDefinitionEntity>()
                .Where(row => row.Id == character.SubraceDefinitionId.Value)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, subrace?.RawJson, "Race version");
        }

        if (character.BackgroundDefinitionId is not null)
        {
            var background = await _database.Table<BackgroundDefinitionEntity>()
                .Where(row => row.Id == character.BackgroundDefinitionId.Value)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, background?.RawJson, string.IsNullOrWhiteSpace(background?.Name) ? "Background" : $"Background {background.Name}");
            await AddGrantedFeatEffectsAsync(effects, background?.FeatsJson, background?.Name ?? "Background");
        }

        foreach (var feat in character.Feats.Where(feat => feat.FeatDefinitionId > 0))
        {
            var featDefinition = await _database.Table<FeatDefinitionEntity>()
                .Where(row => row.Id == feat.FeatDefinitionId)
                .FirstOrDefaultAsync();
            AddOptionEffects(effects, featDefinition?.RawJson, string.IsNullOrWhiteSpace(featDefinition?.Name) ? "Feat" : $"Feat {featDefinition.Name}");
        }

        foreach (var characterClass in character.Classes.Where(characterClass =>
                     characterClass.ClassDefinitionId > 0
                     && characterClass.ClassDefinitionId == character.PrimaryClassDefinitionId))
        {
            await AddClassOptionEffectsAsync(effects, characterClass.ClassDefinitionId);
        }

        return effects;
    }

    public async Task<IReadOnlySet<int>> GetEligibleSpellIdsAsync(int characterId)
    {
        var eligibility = await GetSpellEligibilityAsync(characterId);
        return eligibility.Keys.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<int, SpellEligibility>> GetSpellEligibilityAsync(int characterId)
    {
        await InitializeAsync();

        var characterClasses = await _database.Table<CharacterClassEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        if (characterClasses.Count == 0)
        {
            return new Dictionary<int, SpellEligibility>();
        }

        var spellEntities = await _database.Table<SpellEntity>().ToListAsync();
        var spellsById = spellEntities.ToDictionary(spell => spell.Id);
        var spellsByName = spellEntities
            .GroupBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var progressions = await _database.Table<SpellcastingProgressionEntity>().ToListAsync();
        var eligibility = new Dictionary<int, SpellEligibility>();

        foreach (var characterClass in characterClasses)
        {
            var classDefinition = classDefinitions.FirstOrDefault(definition => definition.Id == characterClass.ClassDefinitionId);
            var progression = classDefinition is null
                ? null
                : progressions.FirstOrDefault(progression => progression.Id == classDefinition.SpellcastingProgressionId);
            var maxSpellLevel = GetMaxSpellLevel(characterClass.Level, progression?.Code ?? "none");
            var rules = await _database.Table<ClassSpellAccessRuleEntity>()
                .Where(rule => rule.ClassDefinitionId == characterClass.ClassDefinitionId)
                .ToListAsync();

            foreach (var rule in rules)
            {
                if (!spellsById.TryGetValue(rule.SpellId, out var ruleSpell)
                    || ruleSpell.Level > maxSpellLevel
                    || !spellsByName.TryGetValue(ruleSpell.Name, out var spellVersions))
                {
                    continue;
                }

                var reason = classDefinition is null
                    ? "Class"
                    : $"{classDefinition.Name} {classDefinition.Source}";

                foreach (var spellVersion in spellVersions.Where(spell => spell.Level <= maxSpellLevel))
                {
                    if (!eligibility.TryGetValue(spellVersion.Id, out var spellEligibility))
                    {
                        spellEligibility = new SpellEligibility { SpellId = spellVersion.Id };
                        eligibility[spellVersion.Id] = spellEligibility;
                    }

                    spellEligibility.Reasons.Add(reason);
                }
            }
        }

        return eligibility;
    }

    public async Task<IReadOnlyList<Spell>> GetCharacterSpellsAsync(int characterId)
    {
        await InitializeAsync();

        var spellLinks = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId)
            .ToListAsync();

        if (spellLinks.Count == 0)
        {
            return [];
        }

        var spellIds = spellLinks.Select(link => link.SpellId).ToHashSet();
        var spells = await GetSpellsAsync();
        return spells
            .Where(spell => spellIds.Contains(spell.Id))
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name)
            .ToList();
    }

    public async Task<IReadOnlySet<int>> GetCharacterSpellIdsAsync(int characterId)
    {
        await InitializeAsync();

        var spellLinks = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId)
            .ToListAsync();

        return spellLinks.Select(link => link.SpellId).ToHashSet();
    }

    public async Task<IReadOnlySet<string>> GetCharacterHiddenFeatureKeysAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterHiddenFeatureEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();

        return rows
            .Select(row => row.FeatureKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetCharacterFeatureHiddenAsync(int characterId, string featureKey, bool isHidden)
    {
        await InitializeAsync();

        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return;
        }

        var normalizedKey = featureKey.Trim();
        var existing = await _database.Table<CharacterHiddenFeatureEntity>()
            .Where(row => row.CharacterId == characterId && row.FeatureKey == normalizedKey)
            .FirstOrDefaultAsync();

        if (isHidden && existing is null)
        {
            await _database.InsertAsync(new CharacterHiddenFeatureEntity
            {
                CharacterId = characterId,
                FeatureKey = normalizedKey
            });
            return;
        }

        if (!isHidden && existing is not null)
        {
            await _database.DeleteAsync(existing);
        }
    }

    public async Task<IReadOnlyDictionary<int, string>> GetCharacterSpellModesAsync(int characterId)
    {
        await InitializeAsync();

        var spellLinks = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId)
            .ToListAsync();

        return spellLinks.ToDictionary(link => link.SpellId, link => string.IsNullOrWhiteSpace(link.Mode) ? "Known" : link.Mode);
    }

    public async Task SetCharacterSpellAsync(int characterId, int spellId, bool isKnown)
    {
        await InitializeAsync();

        var existing = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId && link.SpellId == spellId)
            .FirstOrDefaultAsync();

        if (isKnown && existing is null)
        {
            await _database.InsertAsync(new CharacterSpellEntity
            {
                CharacterId = characterId,
                SpellId = spellId,
                Mode = "Known",
                AddedAt = DateTime.UtcNow
            });
            return;
        }

        if (!isKnown && existing is not null)
        {
            await _database.DeleteAsync(existing);
        }
    }

    public async Task SetCharacterSpellModeAsync(int characterId, int spellId, string mode)
    {
        await InitializeAsync();

        var normalizedMode = mode is "Prepared" ? "Prepared" : "Known";
        var existing = await _database.Table<CharacterSpellEntity>()
            .Where(link => link.CharacterId == characterId && link.SpellId == spellId)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            await _database.InsertAsync(new CharacterSpellEntity
            {
                CharacterId = characterId,
                SpellId = spellId,
                Mode = normalizedMode,
                AddedAt = DateTime.UtcNow
            });
            return;
        }

        existing.Mode = normalizedMode;
        await _database.UpdateAsync(existing);
    }

    public async Task<IReadOnlyList<SpellSlot>> GetCharacterSpellSlotsAsync(int characterId)
    {
        await InitializeAsync();

        var casterLevel = await CalculateNormalCasterLevelAsync(characterId);
        var slotMaximums = GetSlotMaximums(casterLevel);
        var usageRows = await _database.Table<CharacterSpellSlotEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();

        return slotMaximums
            .Select(pair =>
            {
                var used = usageRows.FirstOrDefault(row => row.SpellLevel == pair.Key)?.UsedSlots ?? 0;
                return new SpellSlot
                {
                    SpellLevel = pair.Key,
                    MaxSlots = pair.Value,
                    UsedSlots = Math.Clamp(used, 0, pair.Value)
                };
            })
            .Where(slot => slot.MaxSlots > 0)
            .ToList();
    }

    public async Task SetCharacterSpellSlotUsageAsync(int characterId, int spellLevel, int usedSlots)
    {
        await InitializeAsync();

        var slots = await GetCharacterSpellSlotsAsync(characterId);
        var slot = slots.FirstOrDefault(slot => slot.SpellLevel == spellLevel);
        if (slot is null)
        {
            return;
        }

        var normalizedUsedSlots = Math.Clamp(usedSlots, 0, slot.MaxSlots);
        var entity = await _database.Table<CharacterSpellSlotEntity>()
            .Where(row => row.CharacterId == characterId && row.SpellLevel == spellLevel)
            .FirstOrDefaultAsync();

        if (entity is null)
        {
            await _database.InsertAsync(new CharacterSpellSlotEntity
            {
                CharacterId = characterId,
                SpellLevel = spellLevel,
                UsedSlots = normalizedUsedSlots
            });
            return;
        }

        entity.UsedSlots = normalizedUsedSlots;
        await _database.UpdateAsync(entity);
    }

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

    public async Task<IReadOnlyList<ItemDefinition>> GetItemDefinitionsAsync()
    {
        await InitializeAsync();
        await EnsureItemDataImportedAsync();
        await EnsureSourceSettingsAsync();

        var enabledSources = await GetEnabledSourceCodesInternalAsync();
        var entities = await _database.Table<ItemDefinitionEntity>()
            .OrderBy(item => item.Name)
            .ToListAsync();
        var weaponStatsByItemId = (await _database.Table<ItemWeaponStatEntity>().ToListAsync()).ToDictionary(stat => stat.ItemDefinitionId);
        var armorStatsByItemId = (await _database.Table<ItemArmorStatEntity>().ToListAsync()).ToDictionary(stat => stat.ItemDefinitionId);
        var bonusesByItemId = (await _database.Table<ItemBonusEntity>().ToListAsync()).GroupBy(bonus => bonus.ItemDefinitionId).ToDictionary(group => group.Key, group => group.ToList());
        var attachedSpellsByItemId = (await _database.Table<ItemAttachedSpellEntity>().ToListAsync()).GroupBy(spell => spell.ItemDefinitionId).ToDictionary(group => group.Key, group => group.ToList());
        var requirementsByItemId = (await _database.Table<ItemAttunementRequirementEntity>().ToListAsync()).GroupBy(requirement => requirement.ItemDefinitionId).ToDictionary(group => group.Key, group => group.ToList());
        var properties = await _database.Table<ItemPropertyEntity>().ToListAsync();
        var propertiesById = properties.ToDictionary(property => property.Id);
        var definitionPropertiesByItemId = (await _database.Table<ItemDefinitionPropertyEntity>().ToListAsync())
            .GroupBy(link => link.ItemDefinitionId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return entities
            .Where(entity => enabledSources.Contains(entity.Source))
            .Select(entity => ToModel(
                entity,
                weaponStatsByItemId.GetValueOrDefault(entity.Id),
                armorStatsByItemId.GetValueOrDefault(entity.Id),
                bonusesByItemId.GetValueOrDefault(entity.Id) ?? [],
                attachedSpellsByItemId.GetValueOrDefault(entity.Id) ?? [],
                requirementsByItemId.GetValueOrDefault(entity.Id) ?? [],
                definitionPropertiesByItemId.GetValueOrDefault(entity.Id) ?? [],
                propertiesById))
            .ToList();
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

    private static void TryCopySeedDatabase(string databasePath)
    {
#if SEED_BUILDER
        return;
#else
        if (File.Exists(databasePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var seedStream = OpenAssetAsync(SeedDatabaseAssetName).GetAwaiter().GetResult();
            using var databaseStream = File.Create(databasePath);
            seedStream.CopyTo(databaseStream);
        }
        catch (FileNotFoundException)
        {
            // Development fallback: if no seed database was bundled, InitializeAsync imports from JSON.
        }
#endif
    }

    private static async Task<Stream> OpenAssetAsync(string assetPath)
    {
#if SEED_BUILDER
        return File.OpenRead(Path.Combine("Resources", "Raw", assetPath));
#else
        try
        {
            return await FileSystem.OpenAppPackageFileAsync(assetPath);
        }
        catch (FileNotFoundException) when (File.Exists(Path.Combine("Resources", "Raw", assetPath)))
        {
            return File.OpenRead(Path.Combine("Resources", "Raw", assetPath));
        }
#endif
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

    private async Task EnsureDatabaseVersionAsync()
    {
        var storedVersion = await _database.FindAsync<DatabaseMetadata>("DatabaseVersion");
        if (storedVersion is null || string.IsNullOrWhiteSpace(storedVersion.Value))
        {
            return;
        }

        if (!int.TryParse(storedVersion.Value, out var currentVersion))
        {
            throw new InvalidOperationException($"DatabaseVersion metadata is invalid: '{storedVersion.Value}'.");
        }

        if (currentVersion > DatabaseVersion)
        {
            throw new InvalidOperationException($"Database version {currentVersion} is newer than app-supported version {DatabaseVersion}.");
        }

        if (currentVersion < DatabaseVersion)
        {
            await ApplyMigrationsAsync(currentVersion, DatabaseVersion);
        }
    }

    public async Task<string> ExportCharactersAsync(string filePath)
    {
        await InitializeAsync();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var characters = await GetCharactersAsync();
        var export = new CharacterExport
        {
            ExportedAtUtc = DateTime.UtcNow,
            DatabaseVersion = DatabaseVersion,
            SourceDataVersion = BuildSourceDataVersion(),
            Characters = characters.ToList()
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    private async Task ApplyMigrationsAsync(int fromVersion, int toVersion)
    {
        for (var version = fromVersion + 1; version <= toVersion; version++)
        {
            await ApplyMigrationAsync(version);
            await SetMetadataAsync("DatabaseVersion", version.ToString());
        }
    }

    private static Task ApplyMigrationAsync(int version)
    {
        return version switch
        {
            1 => Task.CompletedTask,
            _ => throw new InvalidOperationException($"No migration is defined for database version {version}.")
        };
    }

    private async Task WriteDatabaseVersionMetadataAsync()
    {
        await SetMetadataAsync("DatabaseVersion", DatabaseVersion.ToString());
        await SetMetadataAsync("SeedDatabaseVersion", SeedDatabaseVersion);
        await SetMetadataAsync("SourceDataVersion", BuildSourceDataVersion());
        await SetMetadataAsync("LastInitializedUtc", DateTime.UtcNow.ToString("O"));
    }

    private async Task SetMetadataAsync(string key, string value)
    {
        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = key, Value = value });
    }

    private static string BuildSourceDataVersion()
    {
        return string.Join(
            "|",
            ImportVersion,
            ClassImportVersion,
            CharacterOptionImportVersion,
            SpellAccessImportVersion,
            ItemImportVersion);
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

    private async Task AddClassOptionEffectsAsync(CharacterOptionEffects effects, int classDefinitionId)
    {
        var classDefinition = await _database.Table<ClassDefinitionEntity>()
            .Where(row => row.Id == classDefinitionId)
            .FirstOrDefaultAsync();
        if (classDefinition is null)
        {
            return;
        }

        await using var indexStream = await OpenAssetAsync("class/index.json");
        using var indexDocument = await JsonDocument.ParseAsync(indexStream);

        foreach (var fileProperty in indexDocument.RootElement.EnumerateObject())
        {
            var fileName = fileProperty.Value.GetString();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            await using var classStream = await OpenAssetAsync($"class/{fileName}");
            using var classDocument = await JsonDocument.ParseAsync(classStream);
            if (!classDocument.RootElement.TryGetProperty("class", out var classes))
            {
                continue;
            }

            foreach (var classElement in classes.EnumerateArray())
            {
                if (!string.Equals(ReadString(classElement, "name"), classDefinition.Name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(ReadString(classElement, "source"), classDefinition.Source, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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

                return;
            }
        }
    }

    private async Task EnsureCharacterColumnsAsync()
    {
        try
        {
            await _database.ExecuteAsync("ALTER TABLE CharacterSpells ADD COLUMN Mode varchar(255) DEFAULT 'Known'");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await _database.ExecuteAsync("ALTER TABLE Characters ADD COLUMN RaceDefinitionId integer");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await _database.ExecuteAsync("ALTER TABLE Characters ADD COLUMN SubraceDefinitionId integer");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await _database.ExecuteAsync("ALTER TABLE Characters ADD COLUMN BackgroundDefinitionId integer");
        }
        catch
        {
            // Column already exists.
        }

        await EnsureCharacterColumnAsync("Strength", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("Dexterity", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("Constitution", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("Intelligence", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("Wisdom", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("Charisma", "integer DEFAULT 10");
        await EnsureCharacterColumnAsync("RaceChoicesJson", "text DEFAULT ''");
        await EnsureCharacterColumnAsync("BackgroundChoicesJson", "text DEFAULT ''");
        await EnsureCharacterColumnAsync("FeatChoicesJson", "text DEFAULT ''");
        await EnsureSpellColumnAsync("IsFavorite", "integer DEFAULT 0");
    }

    private async Task AddGrantedFeatEffectsAsync(CharacterOptionEffects effects, string? featsJson, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(featsJson))
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
                $"Background {sourceName}",
                1,
                $"Grants {featDefinition.Name} ({featDefinition.Source}).");
            AddOptionEffects(effects, featDefinition.RawJson, $"Feat {featDefinition.Name}");
        }
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

    private async Task EnsureCharacterColumnAsync(string columnName, string columnDefinition)
    {
        try
        {
            await _database.ExecuteAsync($"ALTER TABLE Characters ADD COLUMN {columnName} {columnDefinition}");
        }
        catch
        {
            // Column already exists.
        }
    }

    private async Task EnsureSpellColumnAsync(string columnName, string columnDefinition)
    {
        try
        {
            await _database.ExecuteAsync($"ALTER TABLE Spells ADD COLUMN {columnName} {columnDefinition}");
        }
        catch
        {
            // Column already exists.
        }
    }

    private async Task EnsureClassColumnsAsync()
    {
        try
        {
            await _database.ExecuteAsync("ALTER TABLE ClassDefinitions ADD COLUMN RawJson text DEFAULT ''");
        }
        catch
        {
            // Column already exists.
        }
    }

    private async Task EnsureSubclassColumnsAsync()
    {
        try
        {
            await _database.ExecuteAsync("ALTER TABLE SubclassDefinitions ADD COLUMN RawJson text DEFAULT ''");
        }
        catch (SQLiteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private async Task<int> CalculateNormalCasterLevelAsync(int characterId)
    {
        var classRows = await _database.Table<CharacterClassEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var progressions = await _database.Table<SpellcastingProgressionEntity>().ToListAsync();

        var total = 0;
        foreach (var classRow in classRows)
        {
            var classDefinition = classDefinitions.FirstOrDefault(definition => definition.Id == classRow.ClassDefinitionId);
            var progression = classDefinition is null
                ? null
                : progressions.FirstOrDefault(row => row.Id == classDefinition.SpellcastingProgressionId);

            total += progression?.Code switch
            {
                "full" => classRow.Level,
                "1/2" => classRow.Level / 2,
                "artificer" => (int)Math.Ceiling(classRow.Level / 2.0),
                "1/3" => classRow.Level / 3,
                _ => 0
            };
        }

        return Math.Clamp(total, 0, 20);
    }

    private static IReadOnlyDictionary<int, int> GetSlotMaximums(int casterLevel)
    {
        var table = new Dictionary<int, int[]>
        {
            [1] = [2, 0, 0, 0, 0, 0, 0, 0, 0],
            [2] = [3, 0, 0, 0, 0, 0, 0, 0, 0],
            [3] = [4, 2, 0, 0, 0, 0, 0, 0, 0],
            [4] = [4, 3, 0, 0, 0, 0, 0, 0, 0],
            [5] = [4, 3, 2, 0, 0, 0, 0, 0, 0],
            [6] = [4, 3, 3, 0, 0, 0, 0, 0, 0],
            [7] = [4, 3, 3, 1, 0, 0, 0, 0, 0],
            [8] = [4, 3, 3, 2, 0, 0, 0, 0, 0],
            [9] = [4, 3, 3, 3, 1, 0, 0, 0, 0],
            [10] = [4, 3, 3, 3, 2, 0, 0, 0, 0],
            [11] = [4, 3, 3, 3, 2, 1, 0, 0, 0],
            [12] = [4, 3, 3, 3, 2, 1, 0, 0, 0],
            [13] = [4, 3, 3, 3, 2, 1, 1, 0, 0],
            [14] = [4, 3, 3, 3, 2, 1, 1, 0, 0],
            [15] = [4, 3, 3, 3, 2, 1, 1, 1, 0],
            [16] = [4, 3, 3, 3, 2, 1, 1, 1, 0],
            [17] = [4, 3, 3, 3, 2, 1, 1, 1, 1],
            [18] = [4, 3, 3, 3, 3, 1, 1, 1, 1],
            [19] = [4, 3, 3, 3, 3, 2, 1, 1, 1],
            [20] = [4, 3, 3, 3, 3, 2, 2, 1, 1]
        };

        if (!table.TryGetValue(casterLevel, out var slots))
        {
            slots = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        }

        return slots
            .Select((value, index) => new { Level = index + 1, Slots = value })
            .ToDictionary(row => row.Level, row => row.Slots);
    }

    private async Task EnsureClassDataImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("ClassImportVersion");
        var count = await _database.Table<ClassDefinitionEntity>().CountAsync();

        if (import?.Value == ClassImportVersion && count > 0)
        {
            return;
        }

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

        await _database.DeleteAllAsync<RaceDefinitionEntity>();
        await _database.DeleteAllAsync<SubraceDefinitionEntity>();
        await _database.DeleteAllAsync<BackgroundDefinitionEntity>();
        await _database.DeleteAllAsync<FeatDefinitionEntity>();

        await ImportRaceDefinitionsAsync();
        await ImportSubraceDefinitionsAsync();
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
            await _database.InsertAsync(new FeatDefinitionEntity
            {
                Name = ReadString(featElement, "name"),
                Source = ReadString(featElement, "source"),
                Slug = BuildSlug(ReadString(featElement, "name"), ReadString(featElement, "source")),
                Page = ReadInt(featElement, "page"),
                Category = ReadString(featElement, "category"),
                PrerequisiteJson = ReadRawJson(featElement, "prerequisite"),
                AdditionalSpellsJson = ReadRawJson(featElement, "additionalSpells"),
                RawJson = featElement.GetRawText()
            });
        }
    }

    private async Task EnsureSpellAccessImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("SpellAccessImportVersion");
        var count = await _database.Table<ClassSpellAccessRuleEntity>().CountAsync();

        if (import?.Value == SpellAccessImportVersion && count > 0)
        {
            return;
        }

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

    private async Task EnsureItemDataImportedAsync()
    {
        var import = await _database.FindAsync<DatabaseMetadata>("ItemImportVersion");
        var count = await _database.Table<ItemDefinitionEntity>().CountAsync();

        if (import?.Value == ItemImportVersion && count > 0)
        {
            return;
        }

        await _database.DeleteAllAsync<ItemFluffEntity>();
        await _database.DeleteAllAsync<MagicItemVariantEntity>();
        await _database.DeleteAllAsync<ItemGroupMemberEntity>();
        await _database.DeleteAllAsync<ItemGroupEntity>();
        await _database.DeleteAllAsync<ItemAttunementRequirementEntity>();
        await _database.DeleteAllAsync<ItemAttachedSpellEntity>();
        await _database.DeleteAllAsync<ItemBonusEntity>();
        await _database.DeleteAllAsync<ItemDefinitionPropertyEntity>();
        await _database.DeleteAllAsync<ItemTypeEntity>();
        await _database.DeleteAllAsync<ItemPropertyEntity>();
        await _database.DeleteAllAsync<ItemArmorStatEntity>();
        await _database.DeleteAllAsync<ItemWeaponStatEntity>();
        await _database.DeleteAllAsync<ItemDefinitionEntity>();

        await ImportItemBaseDataAsync();
        await ImportMagicItemsAsync();
        await ImportMagicItemVariantsAsync();
        await ImportItemFluffAsync();
        await LinkItemReferencesAsync();

        await _database.InsertOrReplaceAsync(new DatabaseMetadata { Key = "ItemImportVersion", Value = ItemImportVersion });
    }

    private async Task ImportItemBaseDataAsync()
    {
        await using var stream = await OpenAssetAsync("items-base.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (document.RootElement.TryGetProperty("itemType", out var itemTypes))
        {
            foreach (var itemType in itemTypes.EnumerateArray())
            {
                await ImportItemTypeAsync(itemType);
            }
        }

        if (document.RootElement.TryGetProperty("itemProperty", out var itemProperties))
        {
            foreach (var itemProperty in itemProperties.EnumerateArray())
            {
                await ImportItemPropertyAsync(itemProperty);
            }
        }

        if (document.RootElement.TryGetProperty("baseitem", out var baseItems))
        {
            foreach (var itemElement in baseItems.EnumerateArray())
            {
                await ImportItemDefinitionAsync(itemElement, "BaseItem");
            }
        }
    }

    private async Task ImportMagicItemsAsync()
    {
        await using var stream = await OpenAssetAsync("items.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (document.RootElement.TryGetProperty("item", out var items))
        {
            foreach (var itemElement in items.EnumerateArray())
            {
                await ImportItemDefinitionAsync(itemElement, "MagicItem");
            }
        }

        if (document.RootElement.TryGetProperty("itemGroup", out var itemGroups))
        {
            foreach (var itemGroupElement in itemGroups.EnumerateArray())
            {
                await ImportItemGroupAsync(itemGroupElement);
                await ImportItemDefinitionAsync(itemGroupElement, "ItemGroup");
            }
        }
    }

    private async Task ImportItemTypeAsync(JsonElement itemType)
    {
        var abbreviation = ReadString(itemType, "abbreviation");
        var source = ReadString(itemType, "source");
        if (string.IsNullOrWhiteSpace(abbreviation) || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        await _database.InsertAsync(new ItemTypeEntity
        {
            Abbreviation = abbreviation,
            Source = source,
            SourceNameKey = BuildSlug(abbreviation, source),
            Page = ReadInt(itemType, "page"),
            Name = ReadString(itemType, "name"),
            Description = ExtractEntryText(itemType),
            RawJson = itemType.GetRawText()
        });
    }

    private async Task ImportItemPropertyAsync(JsonElement itemProperty)
    {
        var abbreviation = ReadString(itemProperty, "abbreviation");
        var source = ReadString(itemProperty, "source");
        if (string.IsNullOrWhiteSpace(abbreviation) || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        await _database.InsertAsync(new ItemPropertyEntity
        {
            Abbreviation = abbreviation,
            Source = source,
            SourceNameKey = BuildSlug(abbreviation, source),
            Page = ReadInt(itemProperty, "page"),
            Name = ReadString(itemProperty, "name"),
            Description = ExtractEntryText(itemProperty),
            RawJson = itemProperty.GetRawText()
        });
    }

    private async Task ImportItemDefinitionAsync(JsonElement itemElement, string itemKind)
    {
        var name = ReadString(itemElement, "name");
        var source = ReadString(itemElement, "source");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var typeParts = SplitSourceCode(ReadString(itemElement, "type"));
        var entity = new ItemDefinitionEntity
        {
            Name = name,
            Source = source,
            SourceNameKey = BuildItemDefinitionKey(name, source, itemKind, itemElement),
            Page = ReadInt(itemElement, "page"),
            ItemKind = itemKind,
            TypeCode = typeParts.Code,
            TypeSource = typeParts.Source,
            Rarity = ReadString(itemElement, "rarity"),
            Tier = ReadString(itemElement, "tier"),
            RequiresAttunement = HasAttunement(itemElement),
            AttunementText = ReadAttunementText(itemElement),
            Weight = ReadDouble(itemElement, "weight"),
            ValueCopper = ReadInt(itemElement, "value"),
            IsWeapon = ReadBool(itemElement, "weapon"),
            IsArmor = ReadBool(itemElement, "armor"),
            IsWondrous = ReadBool(itemElement, "wondrous"),
            IsConsumable = IsConsumableItem(itemElement),
            IsStaff = ReadBool(itemElement, "staff") || typeParts.Code == "ST",
            IsWand = typeParts.Code == "WD",
            IsPotion = typeParts.Code == "P",
            IsRod = typeParts.Code == "RD",
            IsAmmunition = ReadBool(itemElement, "ammo") || !string.IsNullOrWhiteSpace(ReadString(itemElement, "ammoType")),
            HasFluff = ReadBool(itemElement, "hasFluff"),
            HasFluffImages = ReadBool(itemElement, "hasFluffImages"),
            IsSrd = ReadBool(itemElement, "srd"),
            IsBasicRules = ReadBool(itemElement, "basicRules"),
            IsSrd2024 = ReadBool(itemElement, "srd52"),
            IsBasicRules2024 = ReadBool(itemElement, "basicRules2024"),
            Description = ExtractEntryText(itemElement),
            RawJson = itemElement.GetRawText()
        };

        await _database.InsertAsync(entity);
        await ImportItemDetailsAsync(entity, itemElement);
    }

    private async Task ImportItemDetailsAsync(ItemDefinitionEntity item, JsonElement itemElement)
    {
        if (item.IsWeapon || itemElement.TryGetProperty("dmg1", out _))
        {
            await _database.InsertAsync(new ItemWeaponStatEntity
            {
                ItemDefinitionId = item.Id,
                WeaponCategory = ReadString(itemElement, "weaponCategory"),
                DamageOne = ReadString(itemElement, "dmg1"),
                DamageTwo = ReadString(itemElement, "dmg2"),
                DamageTypeCode = ReadString(itemElement, "dmgType"),
                RangeNormal = ReadRangeValue(itemElement, 0),
                RangeLong = ReadRangeValue(itemElement, 1),
                AmmoType = ReadString(itemElement, "ammoType"),
                Reload = ReadInt(itemElement, "reload"),
                Mastery = ReadString(itemElement, "mastery")
            });
        }

        if (item.IsArmor || itemElement.TryGetProperty("ac", out _))
        {
            await _database.InsertAsync(new ItemArmorStatEntity
            {
                ItemDefinitionId = item.Id,
                ArmorClass = ReadInt(itemElement, "ac"),
                StrengthRequirement = ReadStringOrNumber(itemElement, "strength"),
                HasStealthDisadvantage = ReadBool(itemElement, "stealth"),
                ArmorCategory = item.TypeCode
            });
        }

        await ImportItemDefinitionPropertiesAsync(item.Id, itemElement);
        await ImportItemBonusesAsync(item.Id, itemElement);
        await ImportItemAttunementRequirementsAsync(item.Id, itemElement);
        await ImportItemAttachedSpellsAsync(item.Id, itemElement);
    }

    private async Task ImportItemDefinitionPropertiesAsync(int itemDefinitionId, JsonElement itemElement)
    {
        if (!itemElement.TryGetProperty("property", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var itemProperties = await _database.Table<ItemPropertyEntity>().ToListAsync();
        foreach (var property in properties.EnumerateArray())
        {
            if (property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var propertyCode = property.GetString() ?? "";
            var propertyEntity = itemProperties.FirstOrDefault(row => string.Equals(row.Abbreviation, propertyCode, StringComparison.OrdinalIgnoreCase));
            if (propertyEntity is null)
            {
                continue;
            }

            await _database.InsertAsync(new ItemDefinitionPropertyEntity
            {
                ItemDefinitionId = itemDefinitionId,
                ItemPropertyId = propertyEntity.Id
            });
        }
    }

    private async Task ImportItemBonusesAsync(int itemDefinitionId, JsonElement itemElement)
    {
        var bonusFields = new Dictionary<string, string>
        {
            ["bonusWeapon"] = "Weapon",
            ["bonusAc"] = "ArmorClass",
            ["bonusSpellAttack"] = "SpellAttack",
            ["bonusSpellSaveDc"] = "SpellSaveDc",
            ["bonusSavingThrow"] = "SavingThrow"
        };

        foreach (var bonusField in bonusFields)
        {
            var value = ReadStringOrNumber(itemElement, bonusField.Key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await _database.InsertAsync(new ItemBonusEntity
            {
                ItemDefinitionId = itemDefinitionId,
                BonusType = bonusField.Value,
                Value = value
            });
        }
    }

    private async Task ImportItemAttunementRequirementsAsync(int itemDefinitionId, JsonElement itemElement)
    {
        if (!itemElement.TryGetProperty("reqAttuneTags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in tag.EnumerateObject())
            {
                await _database.InsertAsync(new ItemAttunementRequirementEntity
                {
                    ItemDefinitionId = itemDefinitionId,
                    RequirementType = property.Name,
                    RequirementValue = ReadJsonValueAsString(property.Value),
                    RawJson = tag.GetRawText()
                });
            }
        }
    }

    private async Task ImportItemAttachedSpellsAsync(int itemDefinitionId, JsonElement itemElement)
    {
        if (!itemElement.TryGetProperty("attachedSpells", out var attachedSpells))
        {
            return;
        }

        foreach (var spellReference in FindStringValues(attachedSpells).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parts = SplitSourceCode(spellReference);
            await _database.InsertAsync(new ItemAttachedSpellEntity
            {
                ItemDefinitionId = itemDefinitionId,
                SpellName = parts.Code,
                SpellSource = parts.Source,
                RawJson = attachedSpells.GetRawText()
            });
        }
    }

    private async Task ImportItemGroupAsync(JsonElement itemGroupElement)
    {
        var name = ReadString(itemGroupElement, "name");
        var source = ReadString(itemGroupElement, "source");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var typeParts = SplitSourceCode(ReadString(itemGroupElement, "type"));
        var group = new ItemGroupEntity
        {
            Name = name,
            Source = source,
            SourceNameKey = BuildSlug(name, source),
            Page = ReadInt(itemGroupElement, "page"),
            TypeCode = typeParts.Code,
            Rarity = ReadString(itemGroupElement, "rarity"),
            Description = ExtractEntryText(itemGroupElement),
            RawJson = itemGroupElement.GetRawText()
        };

        await _database.InsertAsync(group);

        if (!itemGroupElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var itemReference in items.EnumerateArray())
        {
            if (itemReference.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var parts = SplitSourceCode(itemReference.GetString() ?? "");
            await _database.InsertAsync(new ItemGroupMemberEntity
            {
                ItemGroupId = group.Id,
                ItemName = parts.Code,
                ItemSource = parts.Source
            });
        }
    }

    private async Task ImportMagicItemVariantsAsync()
    {
        await using var stream = await OpenAssetAsync("magicvariants.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("magicvariant", out var variants))
        {
            return;
        }

        foreach (var variantElement in variants.EnumerateArray())
        {
            var inherits = variantElement.TryGetProperty("inherits", out var inheritsElement) ? inheritsElement : default;
            var source = ReadString(inherits, "source");
            var name = ReadString(variantElement, "name");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var typeParts = SplitSourceCode(ReadString(variantElement, "type"));
            await _database.InsertAsync(new MagicItemVariantEntity
            {
                Name = name,
                Source = source,
                SourceNameKey = BuildSlug(name, source),
                Page = ReadInt(inherits, "page"),
                TypeCode = typeParts.Code,
                NamePrefix = ReadString(inherits, "namePrefix"),
                NameSuffix = ReadString(inherits, "nameSuffix"),
                Rarity = ReadString(inherits, "rarity"),
                Tier = ReadString(inherits, "tier"),
                RequiresAttunement = HasAttunement(inherits),
                BonusWeapon = ReadStringOrNumber(inherits, "bonusWeapon"),
                BonusAc = ReadStringOrNumber(inherits, "bonusAc"),
                Description = ExtractEntryText(inherits),
                RequiresJson = ReadRawJson(variantElement, "requires"),
                ExcludesJson = ReadRawJson(variantElement, "excludes"),
                InheritsJson = inherits.ValueKind == JsonValueKind.Undefined ? "" : inherits.GetRawText(),
                RawJson = variantElement.GetRawText()
            });
        }
    }

    private async Task ImportItemFluffAsync()
    {
        await using var stream = await OpenAssetAsync("fluff-items.json");
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("itemFluff", out var fluffs))
        {
            return;
        }

        foreach (var fluffElement in fluffs.EnumerateArray())
        {
            var name = ReadString(fluffElement, "name");
            var source = ReadString(fluffElement, "source");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            await _database.InsertAsync(new ItemFluffEntity
            {
                ItemName = name,
                ItemSource = source,
                Description = ExtractEntryText(fluffElement),
                ImagesJson = ReadRawJson(fluffElement, "images"),
                RawJson = fluffElement.GetRawText()
            });
        }
    }

    private async Task LinkItemReferencesAsync()
    {
        var itemDefinitions = await _database.Table<ItemDefinitionEntity>().ToListAsync();
        var itemsByKey = itemDefinitions.ToDictionary(item => $"{item.Name}|{item.Source}", StringComparer.OrdinalIgnoreCase);
        var groupMembers = await _database.Table<ItemGroupMemberEntity>().ToListAsync();
        foreach (var groupMember in groupMembers)
        {
            if (itemsByKey.TryGetValue($"{groupMember.ItemName}|{groupMember.ItemSource}", out var itemDefinition))
            {
                groupMember.ItemDefinitionId = itemDefinition.Id;
                await _database.UpdateAsync(groupMember);
            }
        }

        var itemFluffs = await _database.Table<ItemFluffEntity>().ToListAsync();
        foreach (var fluff in itemFluffs)
        {
            if (itemsByKey.TryGetValue($"{fluff.ItemName}|{fluff.ItemSource}", out var itemDefinition))
            {
                fluff.ItemDefinitionId = itemDefinition.Id;
                await _database.UpdateAsync(fluff);
            }
        }
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

    private static Character ToModel(CharacterEntity entity)
    {
        return ToModel(entity, [], [], [], [], [], [], [], [], []);
    }

    private static Character ToModel(CharacterEntity entity, IEnumerable<CharacterClass> classes)
    {
        return ToModel(entity, classes, [], [], [], [], [], [], [], []);
    }

    private static Character ToModel(
        CharacterEntity entity,
        IEnumerable<CharacterClass> classes,
        IEnumerable<CharacterFeat> feats,
        IEnumerable<CharacterSavingThrow> savingThrows,
        IEnumerable<CharacterSkill> skills,
        IEnumerable<CharacterToolProficiency> toolProficiencies,
        IEnumerable<CharacterLanguageProficiency> languageProficiencies,
        IReadOnlyList<RaceDefinitionEntity> raceDefinitions,
        IReadOnlyList<SubraceDefinitionEntity> subraceDefinitions,
        IReadOnlyList<BackgroundDefinitionEntity> backgroundDefinitions)
    {
        var raceDefinition = entity.RaceDefinitionId is null
            ? null
            : raceDefinitions.FirstOrDefault(definition => definition.Id == entity.RaceDefinitionId.Value);
        var backgroundDefinition = entity.BackgroundDefinitionId is null
            ? null
            : backgroundDefinitions.FirstOrDefault(definition => definition.Id == entity.BackgroundDefinitionId.Value);
        var subraceDefinition = entity.SubraceDefinitionId is null
            ? null
            : subraceDefinitions.FirstOrDefault(definition => definition.Id == entity.SubraceDefinitionId.Value);

        return new Character
        {
            Id = entity.Id,
            Name = entity.Name,
            RaceDefinitionId = entity.RaceDefinitionId,
            SubraceDefinitionId = entity.SubraceDefinitionId,
            RaceName = raceDefinition?.Name ?? entity.RaceName,
            RaceSource = raceDefinition?.Source ?? "",
            SubraceName = subraceDefinition?.Name ?? "",
            SubraceSource = subraceDefinition?.Source ?? "",
            RaceChoicesJson = entity.RaceChoicesJson,
            BackgroundDefinitionId = entity.BackgroundDefinitionId,
            BackgroundName = backgroundDefinition?.Name ?? entity.BackgroundName,
            BackgroundSource = backgroundDefinition?.Source ?? "",
            BackgroundChoicesJson = entity.BackgroundChoicesJson,
            FeatChoicesJson = entity.FeatChoicesJson,
            ClassName = entity.ClassName,
            Level = entity.Level,
            Strength = entity.Strength,
            Dexterity = entity.Dexterity,
            Constitution = entity.Constitution,
            Intelligence = entity.Intelligence,
            Wisdom = entity.Wisdom,
            Charisma = entity.Charisma,
            Classes = classes.ToList(),
            Feats = feats.ToList(),
            SavingThrows = MergeSavingThrows(savingThrows),
            Skills = MergeSkills(skills),
            ToolProficiencies = MergeToolProficiencies(toolProficiencies),
            LanguageProficiencies = MergeLanguageProficiencies(languageProficiencies)
        };
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

    private static CharacterClass ToModel(
        CharacterClassEntity entity,
        IReadOnlyList<ClassDefinitionEntity> classDefinitions,
        IReadOnlyList<SubclassDefinitionEntity> subclassDefinitions)
    {
        var classDefinition = classDefinitions.FirstOrDefault(definition => definition.Id == entity.ClassDefinitionId);
        var subclassDefinition = entity.SubclassDefinitionId is null
            ? null
            : subclassDefinitions.FirstOrDefault(definition => definition.Id == entity.SubclassDefinitionId.Value);

        return new CharacterClass
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            ClassDefinitionId = entity.ClassDefinitionId,
            SubclassDefinitionId = entity.SubclassDefinitionId,
            ClassName = classDefinition?.Name ?? "",
            ClassSource = classDefinition?.Source ?? "",
            SubclassName = subclassDefinition?.Name ?? "",
            SubclassSource = subclassDefinition?.Source ?? "",
            Level = entity.Level
        };
    }

    private static CharacterFeat ToModel(CharacterFeatEntity entity, IReadOnlyList<FeatDefinitionEntity> featDefinitions)
    {
        var featDefinition = featDefinitions.FirstOrDefault(definition => definition.Id == entity.FeatDefinitionId);

        return new CharacterFeat
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            FeatDefinitionId = entity.FeatDefinitionId,
            FeatName = featDefinition?.Name ?? "",
            FeatSource = featDefinition?.Source ?? ""
        };
    }

    private static CharacterSavingThrow ToModel(CharacterSavingThrowEntity entity)
    {
        var defaultSavingThrow = CharacterSavingThrow.CreateDefaults()
            .FirstOrDefault(savingThrow => savingThrow.AbilityCode == entity.AbilityCode);

        return new CharacterSavingThrow
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            AbilityCode = entity.AbilityCode,
            AbilityName = defaultSavingThrow?.AbilityName ?? entity.AbilityCode,
            IsProficient = entity.IsProficient,
            RollMode = NormalizeRollMode(entity.RollMode),
            Notes = entity.Notes
        };
    }

    private static CharacterSkill ToModel(CharacterSkillEntity entity)
    {
        var defaultSkill = CharacterSkill.CreateDefaults()
            .FirstOrDefault(skill => string.Equals(skill.Name, entity.Name, StringComparison.OrdinalIgnoreCase));

        return new CharacterSkill
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            Name = entity.Name,
            AbilityCode = defaultSkill?.AbilityCode ?? "",
            AbilityName = defaultSkill?.AbilityName ?? "",
            IsProficient = entity.IsProficient,
            RollMode = NormalizeRollMode(entity.RollMode),
            Notes = entity.Notes
        };
    }

    private static CharacterToolProficiency ToModel(CharacterToolProficiencyEntity entity)
    {
        return new CharacterToolProficiency
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            Name = entity.Name,
            IsProficient = entity.IsProficient,
            Notes = entity.Notes
        };
    }

    private static CharacterLanguageProficiency ToModel(CharacterLanguageProficiencyEntity entity)
    {
        return new CharacterLanguageProficiency
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            Name = entity.Name,
            IsProficient = entity.IsProficient,
            Notes = entity.Notes
        };
    }

    private static SourceSetting ToModel(SourceSettingEntity entity)
    {
        return new SourceSetting
        {
            SourceCode = entity.SourceCode,
            DisplayName = string.IsNullOrWhiteSpace(entity.DisplayName) ? entity.SourceCode : entity.DisplayName,
            IsEnabled = entity.IsEnabled,
            DisplayPriority = entity.DisplayPriority
        };
    }

    private static ItemDefinition ToModel(ItemDefinitionEntity entity)
    {
        return new ItemDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            ItemKind = entity.ItemKind,
            TypeCode = entity.TypeCode,
            Rarity = entity.Rarity,
            Tier = entity.Tier,
            RequiresAttunement = entity.RequiresAttunement,
            AttunementText = entity.AttunementText,
            Weight = entity.Weight,
            ValueCopper = entity.ValueCopper,
            IsWeapon = entity.IsWeapon,
            IsArmor = entity.IsArmor,
            IsWondrous = entity.IsWondrous,
            IsConsumable = entity.IsConsumable,
            Description = entity.Description
        };
    }

    private static ItemDefinition ToModel(
        ItemDefinitionEntity entity,
        ItemWeaponStatEntity? weaponStats,
        ItemArmorStatEntity? armorStats,
        IReadOnlyList<ItemBonusEntity> bonuses,
        IReadOnlyList<ItemAttachedSpellEntity> attachedSpells,
        IReadOnlyList<ItemAttunementRequirementEntity> attunementRequirements,
        IReadOnlyList<ItemDefinitionPropertyEntity> propertyLinks,
        IReadOnlyDictionary<int, ItemPropertyEntity> propertiesById)
    {
        var model = ToModel(entity);
        model.WeaponStats = weaponStats is null
            ? null
            : new ItemWeaponStats
            {
                WeaponCategory = weaponStats.WeaponCategory,
                DamageOne = weaponStats.DamageOne,
                DamageTwo = weaponStats.DamageTwo,
                DamageTypeCode = weaponStats.DamageTypeCode,
                RangeNormal = weaponStats.RangeNormal,
                RangeLong = weaponStats.RangeLong,
                AmmoType = weaponStats.AmmoType,
                Reload = weaponStats.Reload,
                Mastery = weaponStats.Mastery
            };
        model.ArmorStats = armorStats is null
            ? null
            : new ItemArmorStats
            {
                ArmorClass = armorStats.ArmorClass,
                StrengthRequirement = armorStats.StrengthRequirement,
                HasStealthDisadvantage = armorStats.HasStealthDisadvantage,
                ArmorCategory = armorStats.ArmorCategory
            };
        model.Properties = propertyLinks
            .Select(link => propertiesById.TryGetValue(link.ItemPropertyId, out var property) ? property.Name : "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        model.Bonuses = bonuses
            .Select(bonus => new ItemBonus { BonusType = bonus.BonusType, Value = bonus.Value })
            .ToList();
        model.AttachedSpells = attachedSpells
            .Select(spell => string.IsNullOrWhiteSpace(spell.SpellSource) ? spell.SpellName : $"{spell.SpellName} ({spell.SpellSource})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        model.AttunementRequirements = attunementRequirements
            .Select(requirement => $"{requirement.RequirementType}: {requirement.RequirementValue}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        return model;
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

    private async Task<IReadOnlyList<CharacterClass>> GetAllCharacterClassesAsync()
    {
        var classRows = await _database.Table<CharacterClassEntity>().ToListAsync();
        var classDefinitions = await _database.Table<ClassDefinitionEntity>().ToListAsync();
        var subclassDefinitions = await _database.Table<SubclassDefinitionEntity>().ToListAsync();
        return classRows.Select(row => ToModel(row, classDefinitions, subclassDefinitions)).ToList();
    }

    private async Task<IReadOnlyList<CharacterFeat>> GetAllCharacterFeatsAsync()
    {
        var featRows = await _database.Table<CharacterFeatEntity>().ToListAsync();
        var featDefinitions = await _database.Table<FeatDefinitionEntity>().ToListAsync();
        return featRows.Select(row => ToModel(row, featDefinitions)).ToList();
    }

    private async Task<IReadOnlyList<CharacterSavingThrow>> GetAllCharacterSavingThrowsAsync()
    {
        var rows = await _database.Table<CharacterSavingThrowEntity>().ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task<IReadOnlyList<CharacterSkill>> GetAllCharacterSkillsAsync()
    {
        var rows = await _database.Table<CharacterSkillEntity>().ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task<IReadOnlyList<CharacterToolProficiency>> GetAllCharacterToolProficienciesAsync()
    {
        var rows = await _database.Table<CharacterToolProficiencyEntity>().ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task<IReadOnlyList<CharacterLanguageProficiency>> GetAllCharacterLanguageProficienciesAsync()
    {
        var rows = await _database.Table<CharacterLanguageProficiencyEntity>().ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task InsertCharacterSavingThrowsAsync(int characterId, IEnumerable<CharacterSavingThrow> savingThrows)
    {
        var entities = MergeSavingThrows(savingThrows)
            .Select(savingThrow => new CharacterSavingThrowEntity
            {
                CharacterId = characterId,
                AbilityCode = savingThrow.AbilityCode,
                IsProficient = savingThrow.IsProficient,
                RollMode = NormalizeRollMode(savingThrow.RollMode),
                Notes = savingThrow.Notes.Trim()
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
    }

    private async Task InsertCharacterSkillsAsync(int characterId, IEnumerable<CharacterSkill> skills)
    {
        var entities = MergeSkills(skills)
            .Select(skill => new CharacterSkillEntity
            {
                CharacterId = characterId,
                Name = skill.Name,
                IsProficient = skill.IsProficient,
                RollMode = NormalizeRollMode(skill.RollMode),
                Notes = skill.Notes.Trim()
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
    }

    private async Task InsertCharacterToolProficienciesAsync(int characterId, IEnumerable<CharacterToolProficiency> tools)
    {
        var entities = MergeToolProficiencies(tools)
            .Select(tool => new CharacterToolProficiencyEntity
            {
                CharacterId = characterId,
                Name = tool.Name,
                IsProficient = tool.IsProficient,
                Notes = tool.Notes.Trim()
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
    }

    private async Task InsertCharacterLanguageProficienciesAsync(int characterId, IEnumerable<CharacterLanguageProficiency> languages)
    {
        var entities = MergeLanguageProficiencies(languages)
            .Select(language => new CharacterLanguageProficiencyEntity
            {
                CharacterId = characterId,
                Name = language.Name,
                IsProficient = language.IsProficient,
                Notes = language.Notes.Trim()
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
    }

    private static List<CharacterSavingThrow> MergeSavingThrows(IEnumerable<CharacterSavingThrow> savedValues)
    {
        var savedByCode = savedValues.ToDictionary(value => value.AbilityCode, StringComparer.OrdinalIgnoreCase);
        return CharacterSavingThrow.CreateDefaults()
            .Select(defaultValue =>
            {
                if (!savedByCode.TryGetValue(defaultValue.AbilityCode, out var savedValue))
                {
                    return defaultValue;
                }

                defaultValue.Id = savedValue.Id;
                defaultValue.CharacterId = savedValue.CharacterId;
                defaultValue.IsProficient = savedValue.IsProficient;
                defaultValue.RollMode = NormalizeRollMode(savedValue.RollMode);
                defaultValue.Notes = savedValue.Notes;
                return defaultValue;
            })
            .ToList();
    }

    private static List<CharacterSkill> MergeSkills(IEnumerable<CharacterSkill> savedValues)
    {
        var savedByName = savedValues.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        return CharacterSkill.CreateDefaults()
            .Select(defaultValue =>
            {
                if (!savedByName.TryGetValue(defaultValue.Name, out var savedValue))
                {
                    return defaultValue;
                }

                defaultValue.Id = savedValue.Id;
                defaultValue.CharacterId = savedValue.CharacterId;
                defaultValue.IsProficient = savedValue.IsProficient;
                defaultValue.RollMode = NormalizeRollMode(savedValue.RollMode);
                defaultValue.Notes = savedValue.Notes;
                return defaultValue;
            })
            .ToList();
    }

    private static List<CharacterToolProficiency> MergeToolProficiencies(IEnumerable<CharacterToolProficiency> savedValues)
    {
        var savedByName = savedValues.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        return CharacterToolProficiency.CreateDefaults()
            .Select(defaultValue =>
            {
                if (!savedByName.TryGetValue(defaultValue.Name, out var savedValue))
                {
                    return defaultValue;
                }

                defaultValue.Id = savedValue.Id;
                defaultValue.CharacterId = savedValue.CharacterId;
                defaultValue.IsProficient = savedValue.IsProficient;
                defaultValue.Notes = savedValue.Notes;
                return defaultValue;
            })
            .ToList();
    }

    private static List<CharacterLanguageProficiency> MergeLanguageProficiencies(IEnumerable<CharacterLanguageProficiency> savedValues)
    {
        var savedByName = savedValues.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        return CharacterLanguageProficiency.CreateDefaults()
            .Select(defaultValue =>
            {
                if (!savedByName.TryGetValue(defaultValue.Name, out var savedValue))
                {
                    return defaultValue;
                }

                defaultValue.Id = savedValue.Id;
                defaultValue.CharacterId = savedValue.CharacterId;
                defaultValue.IsProficient = savedValue.IsProficient;
                defaultValue.Notes = savedValue.Notes;
                return defaultValue;
            })
            .ToList();
    }

    private static int ClampAbility(int value)
    {
        return Math.Clamp(value, 1, 30);
    }

    private static string NormalizeRollMode(string rollMode)
    {
        return rollMode is "Advantage" or "Disadvantage" ? rollMode : "Normal";
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

    private static string BuildItemDefinitionKey(string name, string source, string itemKind, JsonElement itemElement)
    {
        var type = SplitSourceCode(ReadString(itemElement, "type")).Code;
        var page = ReadInt(itemElement, "page")?.ToString() ?? "";
        return string.Join(
            "|",
            NormalizeSlugPart(itemKind),
            NormalizeSlugPart(name),
            NormalizeSlugPart(source),
            NormalizeSlugPart(type),
            NormalizeSlugPart(page));
    }

    private static string BuildSubclassSlug(string classSlug, string name, string source)
    {
        return $"{classSlug}|{NormalizeSlugPart(name)}|{NormalizeSlugPart(source)}";
    }

    private static string BuildSubraceSlug(string raceName, string raceSource, string name, string source)
    {
        return $"{NormalizeSlugPart(raceName)}|{NormalizeSlugPart(raceSource)}|{NormalizeSlugPart(name)}|{NormalizeSlugPart(source)}";
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

                if (property.NameEquals("choose"))
                {
                    AddChoiceHint(effects, "Abilities", sourceLabel, ReadChoiceCount(property.Value), "Choose ability score bonuses.");
                }
            }
        }
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

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static string ReadStringOrNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return "";
        }

        return ReadJsonValueAsString(property);
    }

    private static string ReadRawJson(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) ? property.GetRawText() : "";
    }

    private static string ReadJsonValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }

    private static (string Code, string Source) SplitSourceCode(string value)
    {
        var parts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? (parts[0], parts[1])
            : (value.Trim(), "");
    }

    private static bool HasAttunement(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("reqAttune", out var property)
            && property.ValueKind is not JsonValueKind.False and not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string ReadAttunementText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("reqAttune", out var property))
        {
            return "";
        }

        return property.ValueKind == JsonValueKind.True ? "true" : ReadJsonValueAsString(property);
    }

    private static bool IsConsumableItem(JsonElement element)
    {
        var type = SplitSourceCode(ReadString(element, "type")).Code;
        return type is "P" or "A" or "AF"
            || ReadBool(element, "ammo")
            || element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("consumable", out var consumable)
                && consumable.ValueKind == JsonValueKind.True;
    }

    private static int? ReadRangeValue(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("range", out var range))
        {
            return null;
        }

        if (range.ValueKind == JsonValueKind.String)
        {
            var parts = range.GetString()?.Split('/', StringSplitOptions.TrimEntries) ?? [];
            return index < parts.Length && int.TryParse(parts[index], out var value) ? value : null;
        }

        if (range.ValueKind == JsonValueKind.Array)
        {
            var values = range.EnumerateArray().ToList();
            return index < values.Count && values[index].ValueKind == JsonValueKind.Number && values[index].TryGetInt32(out var value)
                ? value
                : null;
        }

        return null;
    }

    private static string ExtractEntryText(JsonElement element)
    {
        var parts = new List<string>();
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (element.TryGetProperty("entries", out var entries))
        {
            AppendEntryText(entries, parts);
        }

        if (element.TryGetProperty("additionalEntries", out var additionalEntries))
        {
            AppendEntryText(additionalEntries, parts);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static void AppendEntryText(JsonElement element, List<string> parts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(element.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    AppendEntryText(child, parts);
                }
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    parts.Add(name.GetString() ?? "");
                }

                if (element.TryGetProperty("entries", out var entries))
                {
                    AppendEntryText(entries, parts);
                }
                else if (element.TryGetProperty("items", out var items))
                {
                    AppendEntryText(items, parts);
                }

                break;
        }
    }

    private static IEnumerable<string> FindStringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? "";
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    foreach (var value in FindStringValues(child))
                    {
                        yield return value;
                    }
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in FindStringValues(property.Value))
                    {
                        yield return value;
                    }
                }

                break;
        }
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

    private sealed class CharacterExport
    {
        public DateTime ExportedAtUtc { get; set; }
        public int DatabaseVersion { get; set; }
        public string SourceDataVersion { get; set; } = "";
        public List<Character> Characters { get; set; } = [];
    }
}
