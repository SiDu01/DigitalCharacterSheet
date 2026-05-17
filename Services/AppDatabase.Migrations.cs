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
            await TryRefreshCharacterOptionReferenceDataFromSeedAsync(storedVersion.Value, currentVersion);

            storedVersion = await _database.FindAsync<DatabaseMetadata>("SourceDataVersion");
            if (storedVersion is null || !string.Equals(storedVersion.Value, currentVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The app database was created with source data version '{storedVersion?.Value}', " +
                    $"but this app expects '{currentVersion}'. Runtime JSON imports are disabled because raw 5e Tools data is not packaged. " +
                    "Create a fresh seed database and use a database migration/update path that preserves user-owned character data.");
            }
        }
#endif
    }

#if !SEED_BUILDER
    private async Task TryRefreshCharacterOptionReferenceDataFromSeedAsync(string storedSourceDataVersion, string currentSourceDataVersion)
    {
        var storedParts = storedSourceDataVersion.Split('|');
        var currentParts = currentSourceDataVersion.Split('|');
        if (storedParts.Length != currentParts.Length
            || storedParts[0] != currentParts[0]
            || storedParts[1] != currentParts[1]
            || storedParts[3] != currentParts[3]
            || storedParts[4] != currentParts[4]
            || storedParts[2] == currentParts[2])
        {
            return;
        }

        var seedPath = await CopySeedDatabaseToTemporaryFileAsync();
        var escapedSeedPath = seedPath.Replace("'", "''", StringComparison.Ordinal);

        try
        {
            await _database.ExecuteAsync($"ATTACH DATABASE '{escapedSeedPath}' AS seeddb");

            var seedCharacterOptionVersion = await _database.ExecuteScalarAsync<string>(
                "SELECT Value FROM seeddb.DatabaseMetadata WHERE Key = 'CharacterOptionImportVersion'");
            if (!string.Equals(seedCharacterOptionVersion, CharacterOptionImportVersion, StringComparison.Ordinal))
            {
                return;
            }

            await CreateCharacterOptionReferenceRemapTablesAsync();
            await ReplaceCharacterOptionReferenceTablesFromSeedAsync();
            await RemapCharacterOptionReferencesAsync();
            await SetMetadataAsync("CharacterOptionImportVersion", CharacterOptionImportVersion);
            await SetMetadataAsync("SourceDataVersion", currentSourceDataVersion);
            await SetMetadataAsync("LastReferenceDataRefreshUtc", DateTime.UtcNow.ToString("O"));
        }
        finally
        {
            try
            {
                await _database.ExecuteAsync("DETACH DATABASE seeddb");
            }
            catch
            {
                // Detach can fail if attach did not complete. The temp file cleanup below is still safe to attempt.
            }

            TryDeleteFile(seedPath);
        }
    }

    private static async Task<string> CopySeedDatabaseToTemporaryFileAsync()
    {
        var seedPath = Path.Combine(Path.GetTempPath(), $"digital-character-sheet-seed-{Guid.NewGuid():N}.db3");
        await using var seedStream = await OpenAssetAsync(SeedDatabaseAssetName);
        await using var tempStream = File.Create(seedPath);
        await seedStream.CopyToAsync(tempStream);
        return seedPath;
    }

    private async Task CreateCharacterOptionReferenceRemapTablesAsync()
    {
        await _database.ExecuteAsync("DROP TABLE IF EXISTS CharacterRaceReferenceRemap");
        await _database.ExecuteAsync("DROP TABLE IF EXISTS CharacterSubraceReferenceRemap");
        await _database.ExecuteAsync("DROP TABLE IF EXISTS CharacterBackgroundReferenceRemap");
        await _database.ExecuteAsync("DROP TABLE IF EXISTS CharacterFeatReferenceRemap");

        await _database.ExecuteAsync("""
            CREATE TEMP TABLE CharacterRaceReferenceRemap AS
            SELECT c.Id AS CharacterId, r.Name AS Name, r.Source AS Source
            FROM Characters c
            JOIN RaceDefinitions r ON c.RaceDefinitionId = r.Id
            """);

        await _database.ExecuteAsync("""
            CREATE TEMP TABLE CharacterSubraceReferenceRemap AS
            SELECT c.Id AS CharacterId, s.RaceName AS RaceName, s.RaceSource AS RaceSource, s.Name AS Name, s.Source AS Source
            FROM Characters c
            JOIN SubraceDefinitions s ON c.SubraceDefinitionId = s.Id
            """);

        await _database.ExecuteAsync("""
            CREATE TEMP TABLE CharacterBackgroundReferenceRemap AS
            SELECT c.Id AS CharacterId, b.Name AS Name, b.Source AS Source
            FROM Characters c
            JOIN BackgroundDefinitions b ON c.BackgroundDefinitionId = b.Id
            """);

        await _database.ExecuteAsync("""
            CREATE TEMP TABLE CharacterFeatReferenceRemap AS
            SELECT cf.Id AS CharacterFeatId, f.Name AS Name, f.Source AS Source
            FROM CharacterFeats cf
            JOIN FeatDefinitions f ON cf.FeatDefinitionId = f.Id
            """);
    }

    private async Task ReplaceCharacterOptionReferenceTablesFromSeedAsync()
    {
        await _database.ExecuteAsync("DELETE FROM RaceDefinitions");
        await _database.ExecuteAsync("""
            INSERT INTO RaceDefinitions (Id, Name, Source, Slug, Page, SizeJson, SpeedJson, AbilityJson, LanguageProficienciesJson, TraitTagsJson, RawJson)
            SELECT Id, Name, Source, Slug, Page, SizeJson, SpeedJson, AbilityJson, LanguageProficienciesJson, TraitTagsJson, RawJson
            FROM seeddb.RaceDefinitions
            """);

        await _database.ExecuteAsync("DELETE FROM SubraceDefinitions");
        await _database.ExecuteAsync("""
            INSERT INTO SubraceDefinitions (Id, RaceDefinitionId, RaceName, RaceSource, Name, Source, Slug, Page, RawJson)
            SELECT Id, RaceDefinitionId, RaceName, RaceSource, Name, Source, Slug, Page, RawJson
            FROM seeddb.SubraceDefinitions
            """);

        await _database.ExecuteAsync("DELETE FROM BackgroundDefinitions");
        await _database.ExecuteAsync("""
            INSERT INTO BackgroundDefinitions (Id, Name, Source, Slug, Page, AbilityJson, FeatsJson, SkillProficienciesJson, ToolProficienciesJson, RawJson)
            SELECT Id, Name, Source, Slug, Page, AbilityJson, FeatsJson, SkillProficienciesJson, ToolProficienciesJson, RawJson
            FROM seeddb.BackgroundDefinitions
            """);

        await _database.ExecuteAsync("DELETE FROM FeatDefinitions");
        await _database.ExecuteAsync("""
            INSERT INTO FeatDefinitions (Id, Name, Source, Slug, Page, Category, PrerequisiteJson, AdditionalSpellsJson, AbilityJson, IsRepeatable, RawJson)
            SELECT Id, Name, Source, Slug, Page, Category, PrerequisiteJson, AdditionalSpellsJson, AbilityJson, IsRepeatable, RawJson
            FROM seeddb.FeatDefinitions
            """);
    }

    private async Task RemapCharacterOptionReferencesAsync()
    {
        await _database.ExecuteAsync("""
            UPDATE Characters
            SET RaceDefinitionId = COALESCE((
                SELECT r.Id
                FROM RaceDefinitions r
                JOIN CharacterRaceReferenceRemap m ON r.Name = m.Name AND r.Source = m.Source
                WHERE m.CharacterId = Characters.Id
                LIMIT 1
            ), RaceDefinitionId)
            WHERE Id IN (SELECT CharacterId FROM CharacterRaceReferenceRemap)
            """);

        await _database.ExecuteAsync("""
            UPDATE Characters
            SET SubraceDefinitionId = COALESCE((
                SELECT s.Id
                FROM SubraceDefinitions s
                JOIN CharacterSubraceReferenceRemap m
                    ON s.RaceName = m.RaceName
                    AND s.RaceSource = m.RaceSource
                    AND s.Name = m.Name
                    AND s.Source = m.Source
                WHERE m.CharacterId = Characters.Id
                LIMIT 1
            ), SubraceDefinitionId)
            WHERE Id IN (SELECT CharacterId FROM CharacterSubraceReferenceRemap)
            """);

        await _database.ExecuteAsync("""
            UPDATE Characters
            SET BackgroundDefinitionId = COALESCE((
                SELECT b.Id
                FROM BackgroundDefinitions b
                JOIN CharacterBackgroundReferenceRemap m ON b.Name = m.Name AND b.Source = m.Source
                WHERE m.CharacterId = Characters.Id
                LIMIT 1
            ), BackgroundDefinitionId)
            WHERE Id IN (SELECT CharacterId FROM CharacterBackgroundReferenceRemap)
            """);

        await _database.ExecuteAsync("""
            UPDATE CharacterFeats
            SET FeatDefinitionId = COALESCE((
                SELECT f.Id
                FROM FeatDefinitions f
                JOIN CharacterFeatReferenceRemap m ON f.Name = m.Name AND f.Source = m.Source
                WHERE m.CharacterFeatId = CharacterFeats.Id
                LIMIT 1
            ), FeatDefinitionId)
            WHERE Id IN (SELECT CharacterFeatId FROM CharacterFeatReferenceRemap)
            """);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
#endif

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
            case 8:
                await ApplyFeatDefinitionNormalizationMigrationAsync();
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
