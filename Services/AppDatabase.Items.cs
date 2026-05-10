using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    private static readonly IReadOnlyDictionary<string, string> ItemBonusFields = new Dictionary<string, string>
    {
        ["bonusWeapon"] = "Weapon",
        ["bonusWeaponAttack"] = "WeaponAttack",
        ["bonusWeaponDamage"] = "WeaponDamage",
        ["bonusAc"] = "ArmorClass",
        ["bonusSavingThrow"] = "SavingThrow",
        ["bonusSavingThrowConcentration"] = "ConcentrationSavingThrow",
        ["bonusAbilityCheck"] = "AbilityCheck",
        ["bonusSpellAttack"] = "SpellAttack",
        ["bonusSpellSaveDc"] = "SpellSaveDc",
        ["bonusSpellDamage"] = "SpellDamage",
        ["bonusProficiencyBonus"] = "ProficiencyBonus"
    };

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
        await EnsureGeneratedMagicVariantItemsAsync();
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
            VariantGroupName = "",
            VariantBaseName = "",
            TypeCode = typeParts.Code,
            TypeSource = typeParts.Source,
            Rarity = ReadString(itemElement, "rarity"),
            Tier = ReadString(itemElement, "tier"),
            RequiresAttunement = HasAttunement(itemElement),
            AttunementText = ReadAttunementText(itemElement),
            Weight = ReadDouble(itemElement, "weight"),
            ValueCopper = ReadInt(itemElement, "value"),
            IsWeapon = ReadBool(itemElement, "weapon"),
            IsArmor = ReadBool(itemElement, "armor") || typeParts.Code == "S" || itemElement.TryGetProperty("ac", out _),
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
        foreach (var bonusField in ItemBonusFields)
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
                BonusWeapon = FirstNonEmpty(ReadStringOrNumber(inherits, "bonusWeapon"), ReadStringOrNumber(inherits, "bonusWeaponAttack")),
                BonusAc = ReadStringOrNumber(inherits, "bonusAc"),
                Description = ExtractEntryText(inherits),
                RequiresJson = ReadRawJson(variantElement, "requires"),
                ExcludesJson = ReadRawJson(variantElement, "excludes"),
                InheritsJson = inherits.ValueKind == JsonValueKind.Undefined ? "" : inherits.GetRawText(),
                RawJson = variantElement.GetRawText()
            });
        }
    }

    private async Task ApplyItemBonusCompatibilityMigrationAsync()
    {
        var items = await _database.Table<ItemDefinitionEntity>().ToListAsync();
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.RawJson)))
        {
            using var document = JsonDocument.Parse(item.RawJson);
            foreach (var bonusField in ItemBonusFields)
            {
                var value = ReadStringOrNumber(document.RootElement, bonusField.Key);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                await UpsertGeneratedItemBonusAsync(item.Id, bonusField.Value, value);
            }
        }
    }

    private async Task ApplyMagicVariantItemMigrationAsync()
    {
        await _database.ExecuteAsync("UPDATE ItemDefinitions SET IsArmor = 1 WHERE TypeCode = 'S' OR Id IN (SELECT ItemDefinitionId FROM ItemArmorStats)");
#if SEED_BUILDER
        await EnsureGeneratedMagicVariantItemsAsync();
#else
        await Task.CompletedTask;
#endif
    }

    private async Task EnsureGeneratedMagicVariantItemsAsync()
    {
        var variants = (await _database.Table<MagicItemVariantEntity>().ToListAsync())
            .Where(variant => HasSupportedMagicVariantInheritance(variant))
            .ToList();
        if (variants.Count == 0)
        {
            return;
        }

        var baseItems = (await _database.Table<ItemDefinitionEntity>().ToListAsync())
            .Where(item => item.ItemKind == "BaseItem")
            .ToList();
        if (baseItems.Count == 0)
        {
            return;
        }

        var weaponStatsByItemId = (await _database.Table<ItemWeaponStatEntity>().ToListAsync()).ToDictionary(stat => stat.ItemDefinitionId);
        var armorStatsByItemId = (await _database.Table<ItemArmorStatEntity>().ToListAsync()).ToDictionary(stat => stat.ItemDefinitionId);
        var propertyLinksByItemId = (await _database.Table<ItemDefinitionPropertyEntity>().ToListAsync())
            .GroupBy(link => link.ItemDefinitionId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var variant in variants)
        {
            foreach (var baseItem in baseItems.Where(item => VariantAppliesToBaseItem(variant, item, weaponStatsByItemId.GetValueOrDefault(item.Id))))
            {
                var generatedName = BuildGeneratedVariantItemName(variant, baseItem);
                var existing = await _database.Table<ItemDefinitionEntity>()
                    .Where(item => item.Name == generatedName && item.Source == variant.Source && item.TypeCode == baseItem.TypeCode)
                    .FirstOrDefaultAsync();
                if (existing is not null)
                {
                    existing.IsArmor = existing.IsArmor || baseItem.IsArmor || baseItem.TypeCode == "S";
                    await _database.UpdateAsync(existing);
                    await EnsureGeneratedVariantDetailsAsync(
                        existing.Id,
                        baseItem,
                        weaponStatsByItemId.GetValueOrDefault(baseItem.Id),
                        armorStatsByItemId.GetValueOrDefault(baseItem.Id),
                        propertyLinksByItemId.GetValueOrDefault(baseItem.Id) ?? [],
                        variant);
                    continue;
                }

                var sourceNameKey = BuildGeneratedItemDefinitionKey(generatedName, variant.Source, "MagicItem", baseItem.TypeCode, variant.Page);
                var entity = new ItemDefinitionEntity
                {
                    Name = generatedName,
                    Source = variant.Source,
                    SourceNameKey = sourceNameKey,
                    Page = variant.Page,
                    ItemKind = "MagicItem",
                    VariantGroupName = variant.Name.Replace("(*)", "", StringComparison.Ordinal).Replace("*", "", StringComparison.Ordinal).Trim(),
                    VariantBaseName = baseItem.Name,
                    TypeCode = baseItem.TypeCode,
                    TypeSource = baseItem.Source,
                    Rarity = variant.Rarity,
                    Tier = variant.Tier,
                    RequiresAttunement = variant.RequiresAttunement,
                    AttunementText = "",
                    Weight = baseItem.Weight,
                    ValueCopper = baseItem.ValueCopper,
                    IsWeapon = baseItem.IsWeapon,
                    IsArmor = baseItem.IsArmor || baseItem.TypeCode == "S",
                    IsWondrous = baseItem.IsWondrous,
                    IsConsumable = baseItem.IsConsumable,
                    IsStaff = baseItem.IsStaff,
                    IsWand = baseItem.IsWand,
                    IsPotion = baseItem.IsPotion,
                    IsRod = baseItem.IsRod,
                    IsAmmunition = baseItem.IsAmmunition,
                    HasFluff = false,
                    HasFluffImages = false,
                    IsSrd = baseItem.IsSrd,
                    IsBasicRules = baseItem.IsBasicRules,
                    IsSrd2024 = baseItem.IsSrd2024,
                    IsBasicRules2024 = baseItem.IsBasicRules2024,
                    Description = BuildGeneratedVariantDescription(variant),
                    RawJson = variant.RawJson
                };

                await _database.InsertAsync(entity);
                await EnsureGeneratedVariantDetailsAsync(
                    entity.Id,
                    baseItem,
                    weaponStatsByItemId.GetValueOrDefault(baseItem.Id),
                    armorStatsByItemId.GetValueOrDefault(baseItem.Id),
                    propertyLinksByItemId.GetValueOrDefault(baseItem.Id) ?? [],
                    variant);
            }
        }
    }

    private async Task EnsureGeneratedVariantDetailsAsync(
        int itemDefinitionId,
        ItemDefinitionEntity baseItem,
        ItemWeaponStatEntity? baseWeapon,
        ItemArmorStatEntity? baseArmor,
        IReadOnlyList<ItemDefinitionPropertyEntity> basePropertyLinks,
        MagicItemVariantEntity variant)
    {
        if (baseWeapon is not null)
        {
            var weapon = await _database.Table<ItemWeaponStatEntity>()
                .Where(row => row.ItemDefinitionId == itemDefinitionId)
                .FirstOrDefaultAsync();
            if (weapon is null)
            {
                await _database.InsertAsync(new ItemWeaponStatEntity
                {
                    ItemDefinitionId = itemDefinitionId,
                    WeaponCategory = baseWeapon.WeaponCategory,
                    DamageOne = baseWeapon.DamageOne,
                    DamageTwo = baseWeapon.DamageTwo,
                    DamageTypeCode = baseWeapon.DamageTypeCode,
                    RangeNormal = baseWeapon.RangeNormal,
                    RangeLong = baseWeapon.RangeLong,
                    AmmoType = baseWeapon.AmmoType,
                    Reload = baseWeapon.Reload,
                    Mastery = baseWeapon.Mastery
                });
            }
        }

        if (baseArmor is not null || baseItem.TypeCode == "S")
        {
            var armor = await _database.Table<ItemArmorStatEntity>()
                .Where(row => row.ItemDefinitionId == itemDefinitionId)
                .FirstOrDefaultAsync();
            if (armor is null)
            {
                await _database.InsertAsync(new ItemArmorStatEntity
                {
                    ItemDefinitionId = itemDefinitionId,
                    ArmorClass = baseArmor?.ArmorClass ?? (baseItem.TypeCode == "S" ? 2 : null),
                    StrengthRequirement = baseArmor?.StrengthRequirement ?? "",
                    HasStealthDisadvantage = baseArmor?.HasStealthDisadvantage ?? false,
                    ArmorCategory = baseArmor?.ArmorCategory ?? baseItem.TypeCode
                });
            }
        }

        foreach (var propertyLink in basePropertyLinks)
        {
            var existingLink = await _database.Table<ItemDefinitionPropertyEntity>()
                .Where(row => row.ItemDefinitionId == itemDefinitionId && row.ItemPropertyId == propertyLink.ItemPropertyId)
                .FirstOrDefaultAsync();
            if (existingLink is null)
            {
                await _database.InsertAsync(new ItemDefinitionPropertyEntity
                {
                    ItemDefinitionId = itemDefinitionId,
                    ItemPropertyId = propertyLink.ItemPropertyId
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(variant.BonusAc))
        {
            await UpsertGeneratedItemBonusAsync(itemDefinitionId, "ArmorClass", variant.BonusAc);
        }

        if (!string.IsNullOrWhiteSpace(variant.BonusWeapon))
        {
            await UpsertGeneratedItemBonusAsync(itemDefinitionId, "Weapon", variant.BonusWeapon);
        }
    }

    private async Task PopulateMagicVariantGroupingAsync()
    {
        var generatedItems = (await _database.Table<ItemDefinitionEntity>().ToListAsync())
            .Where(item => item.ItemKind == "MagicItem"
                && !string.IsNullOrWhiteSpace(item.RawJson)
                && string.IsNullOrWhiteSpace(item.VariantGroupName))
            .ToList();

        foreach (var item in generatedItems)
        {
            if (!TryReadMagicVariantGrouping(item.RawJson, item.Name, out var groupName, out var baseName))
            {
                continue;
            }

            item.VariantGroupName = groupName;
            item.VariantBaseName = baseName;
            await _database.UpdateAsync(item);
        }
    }

    private async Task UpsertGeneratedItemBonusAsync(int itemDefinitionId, string bonusType, string value)
    {
        var bonus = await _database.Table<ItemBonusEntity>()
            .Where(row => row.ItemDefinitionId == itemDefinitionId && row.BonusType == bonusType)
            .FirstOrDefaultAsync();
        if (bonus is null)
        {
            await _database.InsertAsync(new ItemBonusEntity
            {
                ItemDefinitionId = itemDefinitionId,
                BonusType = bonusType,
                Value = value
            });
            return;
        }

        if (!string.Equals(bonus.Value, value, StringComparison.Ordinal))
        {
            bonus.Value = value;
            await _database.UpdateAsync(bonus);
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

    private static ItemDefinition ToModel(ItemDefinitionEntity entity)
    {
        return new ItemDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            Source = entity.Source,
            Page = entity.Page,
            ItemKind = entity.ItemKind,
            VariantGroupName = entity.VariantGroupName,
            VariantBaseName = entity.VariantBaseName,
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
            Description = entity.Description,
            RawJson = entity.RawJson
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

    private static string BuildItemDefinitionKey(string name, string source, string itemKind, JsonElement itemElement)
    {
        var type = SplitSourceCode(ReadString(itemElement, "type")).Code;
        var page = ReadInt(itemElement, "page")?.ToString() ?? "";
        return BuildGeneratedItemDefinitionKey(name, source, itemKind, type, page);
    }

    private static string BuildGeneratedItemDefinitionKey(string name, string source, string itemKind, string type, int? page)
    {
        return BuildGeneratedItemDefinitionKey(name, source, itemKind, type, page?.ToString() ?? "");
    }

    private static string BuildGeneratedItemDefinitionKey(string name, string source, string itemKind, string type, string page)
    {
        return string.Join(
            "|",
            NormalizeSlugPart(itemKind),
            NormalizeSlugPart(name),
            NormalizeSlugPart(source),
            NormalizeSlugPart(type),
            NormalizeSlugPart(page));
    }

    private static bool HasSupportedMagicVariantInheritance(MagicItemVariantEntity variant)
    {
        return (!string.IsNullOrWhiteSpace(variant.NamePrefix) || !string.IsNullOrWhiteSpace(variant.NameSuffix))
            && (!string.IsNullOrWhiteSpace(variant.RequiresJson))
            && (!string.IsNullOrWhiteSpace(variant.BonusAc)
                || !string.IsNullOrWhiteSpace(variant.BonusWeapon)
                || !string.IsNullOrWhiteSpace(variant.Description)
                || !string.IsNullOrWhiteSpace(variant.Rarity));
    }

    private static string BuildGeneratedVariantItemName(MagicItemVariantEntity variant, ItemDefinitionEntity baseItem)
    {
        var prefix = variant.NamePrefix.Trim();
        var suffix = variant.NameSuffix.Trim();
        return string.Join(
            " ",
            new[] { prefix, baseItem.Name, suffix }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildGeneratedVariantDescription(MagicItemVariantEntity variant)
    {
        var description = variant.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(variant.BonusWeapon))
            {
                parts.Add($"You have a {variant.BonusWeapon} bonus to attack and damage rolls made with this magic weapon.");
            }

            if (!string.IsNullOrWhiteSpace(variant.BonusAc))
            {
                parts.Add($"You have a {variant.BonusAc} bonus to AC.");
            }

            description = string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        return description
            .Replace("{=bonusAc}", variant.BonusAc, StringComparison.OrdinalIgnoreCase)
            .Replace("{=bonusWeapon}", variant.BonusWeapon, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadMagicVariantGrouping(string rawJson, string generatedName, out string groupName, out string baseName)
    {
        groupName = "";
        baseName = "";

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            groupName = ReadString(document.RootElement, "name")
                .Replace("(*)", "", StringComparison.Ordinal)
                .Replace("*", "", StringComparison.Ordinal)
                .Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return false;
            }

            var inherits = document.RootElement.TryGetProperty("inherits", out var inheritsElement) ? inheritsElement : default;
            var prefix = ReadString(inherits, "namePrefix");
            var suffix = ReadString(inherits, "nameSuffix");
            baseName = generatedName;

            if (!string.IsNullOrWhiteSpace(prefix) && baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[prefix.Length..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(suffix) && baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^suffix.Length].Trim();
            }

            return !string.IsNullOrWhiteSpace(baseName)
                && !string.Equals(baseName, generatedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            groupName = "";
            baseName = "";
            return false;
        }
    }

    private static bool VariantAppliesToBaseItem(MagicItemVariantEntity variant, ItemDefinitionEntity baseItem, ItemWeaponStatEntity? weaponStats)
    {
        if (!string.IsNullOrWhiteSpace(variant.ExcludesJson) && VariantExcludesBaseItem(variant.ExcludesJson, baseItem))
        {
            return false;
        }

        using var document = JsonDocument.Parse(variant.RequiresJson);
        return document.RootElement.ValueKind == JsonValueKind.Array
            && document.RootElement.EnumerateArray().Any(requirement => RequirementMatchesBaseItem(requirement, baseItem, weaponStats));
    }

    private static bool RequirementMatchesBaseItem(JsonElement requirement, ItemDefinitionEntity baseItem, ItemWeaponStatEntity? weaponStats)
    {
        var requiredName = ReadString(requirement, "name");
        if (!string.IsNullOrWhiteSpace(requiredName)
            && !string.Equals(requiredName, baseItem.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var requiredSource = ReadString(requirement, "source");
        if (!string.IsNullOrWhiteSpace(requiredSource)
            && !string.Equals(requiredSource, baseItem.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var requiredType = SplitSourceCode(ReadString(requirement, "type"));
        if (!string.IsNullOrWhiteSpace(requiredType.Code))
        {
            if (!string.Equals(requiredType.Code, baseItem.TypeCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredType.Source)
                && !string.Equals(requiredType.Source, baseItem.Source, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        if (ReadBool(requirement, "armor"))
        {
            return baseItem.IsArmor && baseItem.TypeCode != "S";
        }

        if (ReadBool(requirement, "weapon"))
        {
            return baseItem.IsWeapon || weaponStats is not null;
        }

        if (ReadBool(requirement, "sword"))
        {
            return baseItem.Name.Contains("sword", StringComparison.OrdinalIgnoreCase);
        }

        if (ReadBool(requirement, "net"))
        {
            return baseItem.Name.Equals("Net", StringComparison.OrdinalIgnoreCase);
        }

        var requiredWeaponCategory = ReadString(requirement, "weaponCategory");
        if (!string.IsNullOrWhiteSpace(requiredWeaponCategory))
        {
            return weaponStats?.WeaponCategory.Contains(requiredWeaponCategory, StringComparison.OrdinalIgnoreCase) == true;
        }

        return false;
    }

    private static bool VariantExcludesBaseItem(string excludesJson, ItemDefinitionEntity baseItem)
    {
        using var document = JsonDocument.Parse(excludesJson);
        var excludes = document.RootElement;

        if (ReadBool(excludes, "net") && baseItem.Name.Equals("Net", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!excludes.TryGetProperty("name", out var name))
        {
            return false;
        }

        if (name.ValueKind == JsonValueKind.String)
        {
            return string.Equals(name.GetString(), baseItem.Name, StringComparison.OrdinalIgnoreCase);
        }

        return name.ValueKind == JsonValueKind.Array
            && name.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), baseItem.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
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
}
