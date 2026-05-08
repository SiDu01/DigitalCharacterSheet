namespace DigitalCharacterSheet.Models;

public sealed class ItemGroup
{
    public ItemGroup(IEnumerable<ItemDefinition> versions, IReadOnlyDictionary<string, int>? sourcePriorities = null)
    {
        Versions = versions
            .OrderBy(item => SourceSortKey(item.Source, sourcePriorities))
            .ThenBy(item => item.Source)
            .ThenBy(item => string.IsNullOrWhiteSpace(item.VariantBaseName) ? item.Name : item.VariantBaseName)
            .ToList();

        Primary = Versions.First();
    }

    public ItemDefinition Primary { get; }
    public IReadOnlyList<ItemDefinition> Versions { get; }
    public string Name => Primary.ListName;
    public string Rarity => Primary.Rarity;
    public string TypeCode => Primary.TypeCode;
    public string ItemKind => Primary.ItemKind;
    public bool RequiresAttunement => Versions.Any(item => item.RequiresAttunement);
    public bool IsWeapon => Versions.Any(item => item.IsWeapon);
    public bool IsArmor => Versions.Any(item => item.IsArmor);
    public bool IsConsumable => Versions.Any(item => item.IsConsumable);
    public bool IsWondrous => Versions.Any(item => item.IsWondrous);
    public bool HasVariantOptions => Versions.Any(item => !string.IsNullOrWhiteSpace(item.VariantGroupName));

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
