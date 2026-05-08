using DigitalCharacterSheet.Data;
using DigitalCharacterSheet.Models;
using System.Text.Json;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<Character>> GetCharactersAsync()
    {
        await InitializeAsync();

        var entities = await _database.Table<CharacterEntity>().OrderBy(character => character.Name).ToListAsync();
        var classes = await GetAllCharacterClassesAsync();
        var feats = await GetAllCharacterFeatsAsync();
        var savingThrows = await GetAllCharacterSavingThrowsAsync();
        var skills = await GetAllCharacterSkillsAsync();
        var grantedEffects = await GetAllCharacterGrantedEffectsAsync();
        var fightingStyles = await GetAllCharacterFightingStylesAsync();
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
                grantedEffects.Where(effect => effect.CharacterId == entity.Id),
                fightingStyles.Where(style => style.CharacterId == entity.Id),
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
        var grantedEffects = await GetCharacterGrantedEffectsAsync(entity.Id);
        var fightingStyles = await GetCharacterFightingStylesAsync(entity.Id);
        var toolProficiencies = await GetCharacterToolProficienciesAsync(entity.Id);
        var languageProficiencies = await GetCharacterLanguageProficienciesAsync(entity.Id);
        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var subraceDefinitions = await _database.Table<SubraceDefinitionEntity>().ToListAsync();
        var backgroundDefinitions = await _database.Table<BackgroundDefinitionEntity>().ToListAsync();
        return ToModel(entity, classes, feats, savingThrows, skills, grantedEffects, fightingStyles, toolProficiencies, languageProficiencies, raceDefinitions, subraceDefinitions, backgroundDefinitions);
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

        await PruneInvalidGrantedEffectsAsync(character.Id, character.Classes);

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
        await InsertCharacterFightingStylesAsync(entity.Id, character.FightingStyles);
        await InsertCharacterToolProficienciesAsync(entity.Id, character.ToolProficiencies);
        await InsertCharacterLanguageProficienciesAsync(entity.Id, character.LanguageProficiencies);

        var classes = await GetCharacterClassesAsync(entity.Id);
        var feats = await GetCharacterFeatsAsync(entity.Id);
        var savingThrows = await GetCharacterSavingThrowsAsync(entity.Id);
        var skills = await GetCharacterSkillsAsync(entity.Id);
        var grantedEffects = await GetCharacterGrantedEffectsAsync(entity.Id);
        var fightingStyles = await GetCharacterFightingStylesAsync(entity.Id);
        var toolProficiencies = await GetCharacterToolProficienciesAsync(entity.Id);
        var languageProficiencies = await GetCharacterLanguageProficienciesAsync(entity.Id);
        var raceDefinitions = await _database.Table<RaceDefinitionEntity>().ToListAsync();
        var subraceDefinitions = await _database.Table<SubraceDefinitionEntity>().ToListAsync();
        var backgroundDefinitions = await _database.Table<BackgroundDefinitionEntity>().ToListAsync();
        return ToModel(entity, classes, feats, savingThrows, skills, grantedEffects, fightingStyles, toolProficiencies, languageProficiencies, raceDefinitions, subraceDefinitions, backgroundDefinitions);
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

        var existingFightingStyles = await _database.Table<CharacterFightingStyleEntity>()
            .Where(row => row.CharacterId == character.Id)
            .ToListAsync();
        foreach (var existingFightingStyle in existingFightingStyles)
        {
            await _database.DeleteAsync(existingFightingStyle);
        }

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

        var grantedEffectRows = await _database.Table<CharacterGrantedEffectEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var grantedEffectRow in grantedEffectRows)
        {
            await _database.DeleteAsync(grantedEffectRow);
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

        var fightingStyleRows = await _database.Table<CharacterFightingStyleEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        foreach (var fightingStyleRow in fightingStyleRows)
        {
            await _database.DeleteAsync(fightingStyleRow);
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

    public async Task<IReadOnlyList<CharacterFightingStyle>> GetCharacterFightingStylesAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterFightingStyleEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<CharacterGrantedEffect>> GetCharacterGrantedEffectsAsync(int characterId)
    {
        await InitializeAsync();

        var rows = await _database.Table<CharacterGrantedEffectEntity>().Where(row => row.CharacterId == characterId).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    public async Task AddCharacterGrantedEffectsAsync(int characterId, IEnumerable<CharacterGrantedEffect> effects)
    {
        await InitializeAsync();

        var now = DateTime.UtcNow;
        var existing = await _database.Table<CharacterGrantedEffectEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();
        var existingKeys = existing
            .Select(BuildGrantedEffectKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entities = effects
            .Where(effect => !string.IsNullOrWhiteSpace(effect.EffectType))
            .Select(effect => new CharacterGrantedEffectEntity
            {
                CharacterId = characterId,
                SourceType = effect.SourceType.Trim(),
                SourceDefinitionId = effect.SourceDefinitionId,
                SourceLevel = effect.SourceLevel,
                EffectType = effect.EffectType.Trim(),
                TargetKey = effect.TargetKey.Trim(),
                Value = effect.Value.Trim(),
                Label = effect.Label.Trim(),
                CreatedAt = effect.CreatedAt == default ? now : effect.CreatedAt
            })
            .Where(entity => existingKeys.Add(BuildGrantedEffectKey(entity)))
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
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

    private static Character ToModel(CharacterEntity entity)
    {
        return ToModel(entity, [], [], [], [], [], [], [], [], [], [], []);
    }

    private static Character ToModel(CharacterEntity entity, IEnumerable<CharacterClass> classes)
    {
        return ToModel(entity, classes, [], [], [], [], [], [], [], [], [], []);
    }

    private static Character ToModel(
        CharacterEntity entity,
        IEnumerable<CharacterClass> classes,
        IEnumerable<CharacterFeat> feats,
        IEnumerable<CharacterSavingThrow> savingThrows,
        IEnumerable<CharacterSkill> skills,
        IEnumerable<CharacterGrantedEffect> grantedEffects,
        IEnumerable<CharacterFightingStyle> fightingStyles,
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

        var grantedEffectsList = grantedEffects
            .Where(effect => IsGrantedEffectValidForClasses(effect, classes))
            .GroupBy(BuildGrantedEffectKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var character = new Character
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
            Skills = MergeSkills(skills)
                .Select(ClearGrantedSkillState)
                .ToList(),
            GrantedEffects = grantedEffectsList,
            FightingStyles = fightingStyles
                .GroupBy(style => $"{style.Name}|{style.Source}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(style => style.Name)
                .ToList(),
            ToolProficiencies = MergeToolProficiencies(toolProficiencies),
            LanguageProficiencies = MergeLanguageProficiencies(languageProficiencies)
        };

        ApplyGrantedEffects(character);
        return character;
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
            ProficiencyLevel = NormalizeSkillProficiencyLevel(entity.ProficiencyLevel, entity.IsProficient),
            RollMode = NormalizeRollMode(entity.RollMode),
            Notes = entity.Notes
        };
    }

    private static CharacterFightingStyle ToModel(CharacterFightingStyleEntity entity)
    {
        return new CharacterFightingStyle
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            Name = entity.Name,
            Source = entity.Source,
            Notes = entity.Notes
        };
    }

    private static CharacterGrantedEffect ToModel(CharacterGrantedEffectEntity entity)
    {
        return new CharacterGrantedEffect
        {
            Id = entity.Id,
            CharacterId = entity.CharacterId,
            SourceType = entity.SourceType,
            SourceDefinitionId = entity.SourceDefinitionId,
            SourceLevel = entity.SourceLevel,
            EffectType = entity.EffectType,
            TargetKey = entity.TargetKey,
            Value = entity.Value,
            Label = entity.Label,
            CreatedAt = entity.CreatedAt
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

    private async Task<IReadOnlyList<CharacterGrantedEffect>> GetAllCharacterGrantedEffectsAsync()
    {
        var rows = await _database.Table<CharacterGrantedEffectEntity>().ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private async Task<IReadOnlyList<CharacterFightingStyle>> GetAllCharacterFightingStylesAsync()
    {
        var rows = await _database.Table<CharacterFightingStyleEntity>().ToListAsync();
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
                ProficiencyLevel = NormalizeSkillProficiencyLevel(skill.ProficiencyLevel, skill.IsProficient),
                RollMode = NormalizeRollMode(skill.RollMode),
                Notes = skill.Notes.Trim()
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _database.InsertAllAsync(entities);
        }
    }

    private async Task InsertCharacterFightingStylesAsync(int characterId, IEnumerable<CharacterFightingStyle> fightingStyles)
    {
        var entities = fightingStyles
            .Where(style => !string.IsNullOrWhiteSpace(style.Name))
            .GroupBy(style => $"{style.Name}|{style.Source}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(style => new CharacterFightingStyleEntity
            {
                CharacterId = characterId,
                Name = style.Name.Trim(),
                Source = style.Source.Trim(),
                Notes = style.Notes.Trim()
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
                defaultValue.ProficiencyLevel = NormalizeSkillProficiencyLevel(savedValue.ProficiencyLevel, savedValue.IsProficient);
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

    private static string NormalizeSkillProficiencyLevel(string proficiencyLevel, bool isProficient)
    {
        return proficiencyLevel switch
        {
            "Half" => "Half",
            "Proficient" => "Proficient",
            "Expertise" => "Expertise",
            _ => isProficient ? "Proficient" : "None"
        };
    }

    private static CharacterSkill ClearGrantedSkillState(CharacterSkill skill)
    {
        if (skill.ProficiencyLevel is "Half" or "Expertise")
        {
            skill.ProficiencyLevel = skill.IsProficient ? "Proficient" : "None";
        }

        return skill;
    }

    private static void ApplyGrantedEffects(Character character)
    {
        foreach (var effect in character.GrantedEffects)
        {
            switch (effect.EffectType)
            {
                case "SkillHalfProficiency" when string.Equals(effect.TargetKey, "AllSkills", StringComparison.OrdinalIgnoreCase):
                    foreach (var skill in character.Skills.Where(skill => !skill.IsProficient && skill.ProficiencyLevel is not "Proficient" and not "Expertise"))
                    {
                        skill.ProficiencyLevel = "Half";
                    }
                    break;
                case "SkillExpertise":
                    ApplySkillExpertise(character, effect.TargetKey);
                    break;
                case "FightingStyle":
                    if (character.FightingStyles.All(style => !string.Equals(style.Name, effect.TargetKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        character.FightingStyles.Add(new CharacterFightingStyle
                        {
                            CharacterId = character.Id,
                            Name = effect.TargetKey,
                            Source = effect.Value,
                            Notes = effect.Label
                        });
                    }
                    break;
            }
        }

        character.FightingStyles = character.FightingStyles
            .GroupBy(style => $"{style.Name}|{style.Source}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(style => style.Name)
            .ToList();
    }

    private static void ApplySkillExpertise(Character character, string skillName)
    {
        var skill = character.Skills.FirstOrDefault(skill => string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase));
        if (skill is null)
        {
            return;
        }

        skill.IsProficient = true;
        skill.ProficiencyLevel = "Expertise";
    }

    private async Task PruneInvalidGrantedEffectsAsync(int characterId, IEnumerable<CharacterClass> classes)
    {
        var classList = classes.ToList();
        var existing = await _database.Table<CharacterGrantedEffectEntity>()
            .Where(row => row.CharacterId == characterId)
            .ToListAsync();

        foreach (var effect in existing.Where(effect => !IsGrantedEffectValidForClasses(ToModel(effect), classList)))
        {
            await _database.DeleteAsync(effect);
        }
    }

    private static bool IsGrantedEffectValidForClasses(CharacterGrantedEffect effect, IEnumerable<CharacterClass> classes)
    {
        if (!string.Equals(effect.SourceType, "Class", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(effect.SourceType, "Subclass", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var matchingClass = classes.FirstOrDefault(characterClass => characterClass.ClassDefinitionId == effect.SourceDefinitionId);
        if (matchingClass is null)
        {
            return false;
        }

        return effect.SourceLevel is null || matchingClass.Level >= effect.SourceLevel.Value;
    }

    private static string BuildGrantedEffectKey(CharacterGrantedEffect effect)
    {
        return $"{effect.CharacterId}|{effect.SourceType}|{effect.SourceDefinitionId}|{effect.SourceLevel}|{effect.EffectType}|{effect.TargetKey}|{effect.Value}";
    }

    private static string BuildGrantedEffectKey(CharacterGrantedEffectEntity effect)
    {
        return $"{effect.CharacterId}|{effect.SourceType}|{effect.SourceDefinitionId}|{effect.SourceLevel}|{effect.EffectType}|{effect.TargetKey}|{effect.Value}";
    }
}
