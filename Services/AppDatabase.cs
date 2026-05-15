using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using SQLite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    private const int DatabaseVersion = 7;
    private const int SchemaVersion = 1;
    private const string ImportVersion = "spells-v1";
    private const string ClassImportVersion = "classes-v5";
    private const string CharacterOptionImportVersion = "character-options-v2";
    private const string SpellAccessImportVersion = "class-spell-access-v2";
    private const string ItemImportVersion = "items-v1";
    private const string WikiImportVersion = "wiki-v1";
    private const string SeedDatabaseVersion = "seed-v1";
    private const string DatabaseFileName = "digital-character-sheet.db3";
    private const string SeedDatabaseAssetName = "seed/digital-character-sheet.db3";
#if !SEED_BUILDER
    private readonly bool _useSeedDatabase;
#endif
#if SEED_BUILDER
    private static string seedSourceDataPath = Path.Combine("Resources", "Raw");
#endif
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private readonly SQLiteAsyncConnection _database;
    private readonly SpellImportService _importService;
    private bool _initialized;

    public AppDatabase(SpellImportService importService)
#if SEED_BUILDER
        : this(importService, Path.Combine("bin", DatabaseFileName))
#else
        : this(importService, Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName), useSeedDatabase: true)
#endif
    {
    }

    public AppDatabase(SpellImportService importService, string databasePath, bool useSeedDatabase = false)
    {
        _importService = importService;
#if !SEED_BUILDER
        _useSeedDatabase = useSeedDatabase;
#endif
        if (useSeedDatabase)
        {
            TryCopySeedDatabase(databasePath);
        }

        _database = new SQLiteAsyncConnection(databasePath);
    }

    public static async Task CreateSeedDatabaseAsync(string databasePath)
    {
        await CreateSeedDatabaseAsync(databasePath, null);
    }

    public static async Task CreateSeedDatabaseAsync(string databasePath, string? sourceDataPath)
    {
#if SEED_BUILDER
        if (!string.IsNullOrWhiteSpace(sourceDataPath))
        {
            seedSourceDataPath = sourceDataPath;
            SpellImportService.SeedSourceDataPath = sourceDataPath;
        }
#endif

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var database = new AppDatabase(new SpellImportService(), databasePath);
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
                await CreateTableAsync<CharacterFightingStyleEntity>("CharacterFightingStyles");
                await CreateTableAsync<CharacterToolProficiencyEntity>("CharacterToolProficiencies");
                await CreateTableAsync<CharacterLanguageProficiencyEntity>("CharacterLanguageProficiencies");
                await CreateTableAsync<RaceDefinitionEntity>("RaceDefinitions");
                await CreateTableAsync<SubraceDefinitionEntity>("SubraceDefinitions");
                await CreateTableAsync<BackgroundDefinitionEntity>("BackgroundDefinitions");
                await CreateTableAsync<FeatDefinitionEntity>("FeatDefinitions");
                await CreateTableAsync<CharacterFeatEntity>("CharacterFeats");
                await CreateTableAsync<CharacterHiddenFeatureEntity>("CharacterHiddenFeatures");
                await CreateTableAsync<CharacterGrantedEffectEntity>("CharacterGrantedEffects");
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
                await CreateTableAsync<CharacterInventoryItemEntity>("CharacterInventoryItems");
                await CreateTableAsync<WikiEntryEntity>("WikiEntries");

                initializationStep = "applying database migrations";
                await EnsureDatabaseVersionAsync();
                initializationStep = "checking source data version";
                await EnsureSourceDataVersionAsync();
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
                initializationStep = "importing wiki";
                await EnsureWikiDataImportedAsync();
#else
                initializationStep = "checking wiki";
                await EnsureWikiDataImportedAsync();
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

#if !SEED_BUILDER
#endif

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

    private sealed class CharacterExport
    {
        public int FormatVersion { get; set; } = 2;
        public DateTime ExportedAtUtc { get; set; }
        public int DatabaseVersion { get; set; }
        public string SourceDataVersion { get; set; } = "";
        public List<Character> Characters { get; set; } = [];
        public List<CharacterExportEntry> CharacterEntries { get; set; } = [];
    }

    private sealed class CharacterExportEntry
    {
        public Character Character { get; set; } = new();
        public List<CharacterSpellExport> Spells { get; set; } = [];
        public List<CharacterSpellSlotExport> SpellSlots { get; set; } = [];
        public List<string> HiddenFeatureKeys { get; set; } = [];
        public List<CharacterInventoryItemExport> Inventory { get; set; } = [];
    }

    private sealed class CharacterSpellExport
    {
        public int SpellId { get; set; }
        public string Name { get; set; } = "";
        public string Source { get; set; } = "";
        public int? Page { get; set; }
        public string Mode { get; set; } = "Known";
    }

    private sealed class CharacterSpellSlotExport
    {
        public int SpellLevel { get; set; }
        public int UsedSlots { get; set; }
    }

    private sealed class CharacterInventoryItemExport
    {
        public int? ItemDefinitionId { get; set; }
        public string ItemName { get; set; } = "";
        public string ItemSource { get; set; } = "";
        public string CustomName { get; set; } = "";
        public string CustomDescription { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public bool IsEquipped { get; set; }
        public bool IsAttuned { get; set; }
        public bool IsCarried { get; set; } = true;
        public string ContainerName { get; set; } = "";
        public string Notes { get; set; } = "";
        public int? CurrentCharges { get; set; }
        public int? MaxCharges { get; set; }
    }
}
