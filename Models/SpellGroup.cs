namespace DigitalCharacterSheet.Models;

public sealed class SpellGroup
{
    public SpellGroup(IEnumerable<Spell> versions, IReadOnlyDictionary<string, int>? sourcePriorities = null)
    {
        Versions = versions
            .OrderBy(spell => SourceSortKey(spell.Source, sourcePriorities))
            .ThenBy(spell => spell.Source)
            .ToList();

        Primary = Versions.First();
    }

    public Spell Primary { get; }
    public IReadOnlyList<Spell> Versions { get; }
    public string Name => Primary.Name;
    public int Level => Primary.Level;
    public string LevelDisplay => Primary.LevelDisplay;
    public string School => Primary.School;
    public string CastingTimeDisplay => Primary.CastingTimeDisplay;
    public string RangeDisplay => Primary.RangeDisplay;
    public string SourceDisplay => string.Join(", ", Versions.Select(spell => spell.Source).Distinct());

    public bool IsPrepared => Versions.Any(spell => spell.IsPrepared);
    public bool IsFavorite => Versions.Any(spell => spell.IsFavorite);

    public bool ContainsSpell(int spellId)
    {
        return Versions.Any(spell => spell.Id == spellId);
    }

    public Spell ResolveVersion(Spell? selectedSpell)
    {
        if (selectedSpell is not null)
        {
            var matchingVersion = Versions.FirstOrDefault(spell => spell.Id == selectedSpell.Id);
            if (matchingVersion is not null)
            {
                return matchingVersion;
            }
        }

        return Primary;
    }

    private static int SourceSortKey(string source, IReadOnlyDictionary<string, int>? sourcePriorities)
    {
        if (sourcePriorities is not null && sourcePriorities.TryGetValue(source, out var priority))
        {
            return priority;
        }

        return source switch
        {
            "XPHB" => 0,
            "PHB" => 1,
            _ => 1000
        };
    }
}
