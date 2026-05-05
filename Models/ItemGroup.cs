namespace DigitalCharacterSheet.Models;

public sealed class ItemGroup
{
    public ItemGroup(IEnumerable<ItemDefinition> versions, IReadOnlyDictionary<string, int>? sourcePriorities = null)
    {
        Versions = versions
            .OrderBy(item => SourceSortKey(item.Source, sourcePriorities))
            .ThenBy(item => item.Source)
            .ToList();

        Primary = Versions.First();
    }

    public ItemDefinition Primary { get; }
    public IReadOnlyList<ItemDefinition> Versions { get; }
    public string Name => Primary.Name;
    public string Rarity => Primary.Rarity;
    public string TypeCode => Primary.TypeCode;
    public string ItemKind => Primary.ItemKind;
    public bool RequiresAttunement => Versions.Any(item => item.RequiresAttunement);
    public bool IsWeapon => Versions.Any(item => item.IsWeapon);
    public bool IsArmor => Versions.Any(item => item.IsArmor);
    public bool IsConsumable => Versions.Any(item => item.IsConsumable);
    public bool IsWondrous => Versions.Any(item => item.IsWondrous);

    public ItemDefinition ResolveVersion(ItemDefinition? selectedItem)
    {
        if (selectedItem is not null)
        {
            var matchingVersion = Versions.FirstOrDefault(item => item.Id == selectedItem.Id);
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
            "XDMG" => 2,
            "DMG" => 3,
            _ => 1000
        };
    }
}
