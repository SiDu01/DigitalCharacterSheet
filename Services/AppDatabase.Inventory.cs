using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<CharacterInventoryItem>> GetCharacterInventoryAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterInventoryItemEntity>()
            .Where(row => row.CharacterId == characterId)
            .OrderBy(row => row.CustomName)
            .ToListAsync();
        var definitions = (await GetItemDefinitionsAsync()).ToDictionary(item => item.Id);

        return rows
            .Select(row => ToModel(row, definitions.GetValueOrDefault(row.ItemDefinitionId ?? 0)))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.DisplayName)
            .ToList();
    }

    public async Task<CharacterInventoryItem> AddCharacterInventoryItemAsync(int characterId, int itemDefinitionId)
    {
        await InitializeAsync();

        var item = await _database.Table<ItemDefinitionEntity>()
            .Where(row => row.Id == itemDefinitionId)
            .FirstOrDefaultAsync();
        if (item is null)
        {
            throw new InvalidOperationException("Item definition was not found.");
        }

        var now = DateTime.UtcNow;
        var entity = new CharacterInventoryItemEntity
        {
            CharacterId = characterId,
            ItemDefinitionId = itemDefinitionId,
            CustomName = item.Name,
            Quantity = 1,
            IsCarried = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _database.InsertAsync(entity);
        var definitions = (await GetItemDefinitionsAsync()).ToDictionary(definition => definition.Id);
        return ToModel(entity, definitions.GetValueOrDefault(itemDefinitionId));
    }

    public async Task UpdateCharacterInventoryItemAsync(CharacterInventoryItem item)
    {
        await InitializeAsync();

        var entity = await _database.Table<CharacterInventoryItemEntity>()
            .Where(row => row.Id == item.Id)
            .FirstOrDefaultAsync();
        if (entity is null)
        {
            throw new InvalidOperationException("Inventory item was not found.");
        }

        entity.CustomName = item.CustomName.Trim();
        entity.CustomDescription = item.CustomDescription.Trim();
        entity.Quantity = Math.Max(1, item.Quantity);
        entity.IsEquipped = item.IsEquipped;
        entity.IsAttuned = item.RequiresAttunement && item.IsAttuned;
        entity.IsCarried = item.IsCarried;
        entity.ContainerName = item.ContainerName.Trim();
        entity.Notes = item.Notes.Trim();
        entity.CurrentCharges = item.CurrentCharges;
        entity.MaxCharges = item.MaxCharges;
        entity.UpdatedAt = DateTime.UtcNow;

        await _database.UpdateAsync(entity);
    }

    public async Task RemoveCharacterInventoryItemAsync(int inventoryItemId)
    {
        await InitializeAsync();

        var entity = await _database.Table<CharacterInventoryItemEntity>()
            .Where(row => row.Id == inventoryItemId)
            .FirstOrDefaultAsync();
        if (entity is not null)
        {
            await _database.DeleteAsync(entity);
        }
    }

    private async Task DeleteCharacterInventoryAsync(int characterId)
    {
        var rows = await _database.Table<CharacterInventoryItemEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var row in rows)
        {
            await _database.DeleteAsync(row);
        }
    }

    private static CharacterInventoryItem ToModel(CharacterInventoryItemEntity entity, ItemDefinition? itemDefinition)
    {
        return new CharacterInventoryItem
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            ItemDefinitionId = entity.ItemDefinitionId,
            ItemDefinition = itemDefinition,
            CustomName = entity.CustomName,
            CustomDescription = entity.CustomDescription,
            Quantity = Math.Max(1, entity.Quantity),
            IsEquipped = entity.IsEquipped,
            IsAttuned = itemDefinition?.RequiresAttunement == true && entity.IsAttuned,
            IsCarried = entity.IsCarried,
            ContainerName = entity.ContainerName,
            Notes = entity.Notes,
            CurrentCharges = entity.CurrentCharges,
            MaxCharges = entity.MaxCharges
        };
    }
}
