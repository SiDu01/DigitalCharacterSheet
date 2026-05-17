using SQLite;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    private async Task ApplyLegacyColumnCompatibilityMigrationAsync()
    {
        await AddColumnIfMissingAsync("CharacterSpells", "Mode", "varchar(255) DEFAULT 'Known'");
        await AddColumnIfMissingAsync("Characters", "RaceDefinitionId", "integer");
        await AddColumnIfMissingAsync("Characters", "SubraceDefinitionId", "integer");
        await AddColumnIfMissingAsync("Characters", "BackgroundDefinitionId", "integer");
        await AddColumnIfMissingAsync("Characters", "Strength", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "Dexterity", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "Constitution", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "Intelligence", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "Wisdom", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "Charisma", "integer DEFAULT 10");
        await AddColumnIfMissingAsync("Characters", "MaxHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "CurrentHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "TemporaryHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "ConditionsJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("Characters", "RaceChoicesJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("Characters", "BackgroundChoicesJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("Characters", "FeatChoicesJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("Spells", "IsFavorite", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("CharacterSkills", "ProficiencyLevel", "text DEFAULT 'None'");
        await AddColumnIfMissingAsync("ClassDefinitions", "RawJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("SubclassDefinitions", "RawJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("FeatDefinitions", "AbilityJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("FeatDefinitions", "IsRepeatable", "integer DEFAULT 0");
    }

    private async Task ApplyMagicVariantGroupingMigrationAsync()
    {
        await AddColumnIfMissingAsync("ItemDefinitions", "VariantGroupName", "text DEFAULT ''");
        await AddColumnIfMissingAsync("ItemDefinitions", "VariantBaseName", "text DEFAULT ''");
#if SEED_BUILDER
        await PopulateMagicVariantGroupingAsync();
#else
        await Task.CompletedTask;
#endif
    }

    private async Task ApplyCharacterCombatStateMigrationAsync()
    {
        await AddColumnIfMissingAsync("Characters", "MaxHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "CurrentHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "TemporaryHitPoints", "integer DEFAULT 0");
        await AddColumnIfMissingAsync("Characters", "ConditionsJson", "text DEFAULT ''");
    }

    private async Task ApplyFeatDefinitionNormalizationMigrationAsync()
    {
        await AddColumnIfMissingAsync("FeatDefinitions", "AbilityJson", "text DEFAULT ''");
        await AddColumnIfMissingAsync("FeatDefinitions", "IsRepeatable", "integer DEFAULT 0");
    }

    private async Task AddColumnIfMissingAsync(string tableName, string columnName, string columnDefinition)
    {
        var columns = await _database.QueryAsync<TableColumnInfo>($"PRAGMA table_info({tableName})");
        if (columns.Any(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _database.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
    }

    private sealed class TableColumnInfo
    {
        [Column("name")]
        public string Name { get; set; } = "";
    }
}
