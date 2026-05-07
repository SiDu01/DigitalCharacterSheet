using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalCharacterSheet.Models;

namespace DigitalCharacterSheet.Services;

public sealed partial class SpellImportService
{
#if SEED_BUILDER
    public static string SeedSourceDataPath { get; set; } = Path.Combine("Resources", "Raw");
#endif

    public async Task<IReadOnlyList<Spell>> LoadBundledSpellsAsync()
    {
        var fileNames = await LoadSpellFileNamesAsync();
        var spells = new List<Spell>();

        foreach (var fileName in fileNames)
        {
            await using var stream = await OpenAssetAsync($"spells/{fileName}");
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("spell", out var spellArray))
            {
                continue;
            }

            foreach (var spellElement in spellArray.EnumerateArray())
            {
                spells.Add(MapSpell(spellElement));
            }
        }

        return spells
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name)
            .ThenBy(spell => spell.Source)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> LoadSpellFileNamesAsync()
    {
        await using var stream = await OpenAssetAsync("spells/index.json");
        using var document = await JsonDocument.ParseAsync(stream);

        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
    }

    private static async Task<Stream> OpenAssetAsync(string assetPath)
    {
#if SEED_BUILDER
        return await Task.FromResult<Stream>(File.OpenRead(Path.Combine(SeedSourceDataPath, assetPath)));
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

    private static Spell MapSpell(JsonElement element)
    {
        var schoolCode = ReadString(element, "school");
        var spell = new Spell
        {
            Name = ReadString(element, "name"),
            Source = ReadString(element, "source"),
            Page = ReadInt(element, "page"),
            Level = ReadInt(element, "level") ?? 0,
            SchoolCode = schoolCode,
            School = MapSchool(schoolCode),
            Description = ReadEntries(element, "entries"),
            UpCast = ReadEntries(element, "entriesHigherLevel"),
            IsSrd = ReadBool(element, "srd"),
            IsBasicRules = ReadBool(element, "basicRules"),
            IsSrd2024 = ReadBool(element, "srd52"),
            IsBasicRules2024 = ReadBool(element, "basicRules2024"),
            HasFluff = ReadBool(element, "hasFluff"),
            HasFluffImages = ReadBool(element, "hasFluffImages"),
            RawJson = element.GetRawText()
        };

        MapCastingTime(element, spell);
        MapRange(element, spell);
        MapComponents(element, spell);
        MapDuration(element, spell);
        MapMeta(element, spell);
        MapTags(element, spell);

        return spell;
    }

    private static void MapCastingTime(JsonElement element, Spell spell)
    {
        if (!element.TryGetProperty("time", out var timeArray) || timeArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var firstTime = timeArray.EnumerateArray().FirstOrDefault();
        if (firstTime.ValueKind == JsonValueKind.Undefined)
        {
            return;
        }

        spell.CastingTimeNumber = ReadInt(firstTime, "number");
        spell.CastingTimeUnit = ReadString(firstTime, "unit");
        spell.CastingTimeCondition = ReadString(firstTime, "condition");
        spell.CastingTimeNote = ReadString(firstTime, "note");
    }

    private static void MapRange(JsonElement element, Spell spell)
    {
        if (!element.TryGetProperty("range", out var range) || range.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        spell.RangeType = ReadString(range, "type");

        if (range.TryGetProperty("distance", out var distance))
        {
            spell.RangeDistanceType = ReadString(distance, "type");
            spell.RangeDistanceAmount = ReadInt(distance, "amount");
        }
    }

    private static void MapComponents(JsonElement element, Spell spell)
    {
        if (!element.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        spell.HasVerbalComponent = ReadBool(components, "v");
        spell.HasSomaticComponent = ReadBool(components, "s");
        spell.HasRoyaltyComponent = ReadBool(components, "r");

        if (!components.TryGetProperty("m", out var material))
        {
            return;
        }

        spell.HasMaterialComponent = true;

        if (material.ValueKind == JsonValueKind.String)
        {
            spell.MaterialComponent = material.GetString() ?? "";
            return;
        }

        if (material.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        spell.MaterialComponent = ReadString(material, "text");
        spell.MaterialCostCopper = ReadInt(material, "cost");
        spell.ConsumesMaterial = ReadBool(material, "consume");
    }

    private static void MapDuration(JsonElement element, Spell spell)
    {
        if (!element.TryGetProperty("duration", out var durationArray) || durationArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var firstDuration = durationArray.EnumerateArray().FirstOrDefault();
        if (firstDuration.ValueKind == JsonValueKind.Undefined)
        {
            return;
        }

        spell.DurationType = ReadString(firstDuration, "type");
        spell.RequiresConcentration = ReadBool(firstDuration, "concentration");

        if (firstDuration.TryGetProperty("duration", out var duration))
        {
            spell.DurationAmount = ReadInt(duration, "amount");
            spell.DurationUnit = ReadString(duration, "type");
        }

        if (firstDuration.TryGetProperty("ends", out var ends) && ends.ValueKind == JsonValueKind.Array)
        {
            spell.DurationEnds = string.Join(", ", ReadStringArray(ends));
        }
    }

    private static void MapMeta(JsonElement element, Spell spell)
    {
        if (element.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            spell.IsRitual = ReadBool(meta, "ritual");
        }
    }

    private static void MapTags(JsonElement element, Spell spell)
    {
        spell.SavingThrows = ReadStringArray(element, "savingThrow");
        spell.AbilityChecks = ReadStringArray(element, "abilityCheck");
        spell.DamageTypes = ReadStringArray(element, "damageInflict");
        spell.DamageResistances = ReadStringArray(element, "damageResist");
        spell.DamageImmunities = ReadStringArray(element, "damageImmune");
        spell.DamageVulnerabilities = ReadStringArray(element, "damageVulnerable");
        spell.ConditionsInflicted = ReadStringArray(element, "conditionInflict");
        spell.ConditionImmunities = ReadStringArray(element, "conditionImmune");
        spell.SpellAttackTypes = ReadStringArray(element, "spellAttack");
        spell.AffectsCreatureTypes = ReadStringArray(element, "affectsCreatureType");
        spell.MiscTags = ReadStringArray(element, "miscTags");
        spell.AreaTags = ReadStringArray(element, "areaTags");
        spell.Aliases = ReadStringArray(element, "alias");
    }

    private static string ReadEntries(JsonElement element, string propertyName)
    {
        return !element.TryGetProperty(propertyName, out var entries)
            ? ""
            : NormalizeWhitespace(FlattenEntry(entries));
    }

    private static string FlattenEntry(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => CleanMarkup(element.GetString() ?? ""),
            JsonValueKind.Array => string.Join("\n\n", element.EnumerateArray().Select(FlattenEntry).Where(value => !string.IsNullOrWhiteSpace(value))),
            JsonValueKind.Object => FlattenObjectEntry(element),
            _ => ""
        };
    }

    private static string FlattenObjectEntry(JsonElement element)
    {
        var parts = new List<string>();

        if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            parts.Add(name.GetString() ?? "");
        }

        if (element.TryGetProperty("entries", out var entries))
        {
            parts.Add(FlattenEntry(entries));
        }

        if (element.TryGetProperty("items", out var items))
        {
            parts.Add(FlattenEntry(items));
        }

        if (element.TryGetProperty("by", out var by) && by.ValueKind == JsonValueKind.String)
        {
            parts.Add($"- {by.GetString()}");
        }

        return string.Join("\n", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string CleanMarkup(string value)
    {
        return MarkupRegex().Replace(value, match => match.Groups["text"].Value.Split('|')[0]);
    }

    private static string NormalizeWhitespace(string value)
    {
        return LineBreakRegex().Replace(value.Trim(), "\n\n");
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? ReadStringArray(property)
            : [];
    }

    private static List<string> ReadStringArray(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MapSchool(string schoolCode)
    {
        return schoolCode switch
        {
            "A" => "Abjuration",
            "C" => "Conjuration",
            "D" => "Divination",
            "E" => "Enchantment",
            "I" => "Illusion",
            "N" => "Necromancy",
            "T" => "Transmutation",
            "V" => "Evocation",
            _ => schoolCode
        };
    }

    [GeneratedRegex(@"\{@[a-zA-Z]+ (?<text>[^}]+)\}")]
    private static partial Regex MarkupRegex();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex LineBreakRegex();
}
