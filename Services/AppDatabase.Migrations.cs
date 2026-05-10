using DigitalCharacterSheet.Data;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    private async Task EnsureDatabaseVersionAsync()
    {
        var storedVersion = await _database.FindAsync<DatabaseMetadata>("DatabaseVersion");
        var currentVersion = 0;

        if (storedVersion is not null
            && !string.IsNullOrWhiteSpace(storedVersion.Value)
            && !int.TryParse(storedVersion.Value, out currentVersion))
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

    private async Task EnsureSourceDataVersionAsync()
    {
#if SEED_BUILDER
        await Task.CompletedTask;
#else
        if (!_useSeedDatabase)
        {
            return;
        }

        var storedVersion = await _database.FindAsync<DatabaseMetadata>("SourceDataVersion");
        if (storedVersion is null || string.IsNullOrWhiteSpace(storedVersion.Value))
        {
            return;
        }

        var currentVersion = BuildSourceDataVersion();
        if (!string.Equals(storedVersion.Value, currentVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The app database was created with source data version '{storedVersion.Value}', " +
                $"but this app expects '{currentVersion}'. Runtime JSON imports are disabled because raw 5e Tools data is not packaged. " +
                "Create a fresh seed database and use a database migration/update path that preserves user-owned character data.");
        }
#endif
    }

    private async Task ApplyMigrationsAsync(int fromVersion, int toVersion)
    {
        for (var version = fromVersion + 1; version <= toVersion; version++)
        {
            await ApplyMigrationAsync(version);
            await SetMetadataAsync("DatabaseVersion", version.ToString());
            await SetMetadataAsync("LastMigratedUtc", DateTime.UtcNow.ToString("O"));
        }
    }

    private async Task ApplyMigrationAsync(int version)
    {
        switch (version)
        {
            case 1:
                return;
            case 2:
                await ApplyLegacyColumnCompatibilityMigrationAsync();
                return;
            case 3:
                await _database.CreateTableAsync<CharacterInventoryItemEntity>();
                return;
            case 4:
                await ApplyMagicVariantItemMigrationAsync();
                return;
            case 5:
                await ApplyMagicVariantGroupingMigrationAsync();
                return;
            case 6:
                await ApplyItemBonusCompatibilityMigrationAsync();
                return;
            case 7:
                await ApplyCharacterCombatStateMigrationAsync();
                return;
            default:
                throw new InvalidOperationException($"No migration is defined for database version {version}.");
        }
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

    private static InvalidOperationException BuildReferenceDataVersionMismatchException(
        string dataName,
        string metadataKey,
        string? storedVersion,
        string expectedVersion)
    {
        var actualVersion = string.IsNullOrWhiteSpace(storedVersion) ? "<missing>" : storedVersion;
        return new InvalidOperationException(
            $"The bundled seed database has outdated or missing {dataName} reference data. " +
            $"{metadataKey} is '{actualVersion}', but the app expects '{expectedVersion}'. " +
            "Runtime JSON imports are disabled because raw 5e Tools data is not packaged. " +
            "Refresh Resources\\Raw\\seed\\digital-character-sheet.db3 with Tools\\SeedDatabaseBuilder before building the app.");
    }
}
