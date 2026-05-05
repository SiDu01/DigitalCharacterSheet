namespace DigitalCharacterSheet.Models;

public sealed class ClassGroup
{
    public ClassGroup(IEnumerable<ClassDefinition> versions)
    {
        Versions = versions
            .OrderBy(definition => SourceSortKey(definition.Source))
            .ThenBy(definition => definition.Source)
            .ToList();

        Primary = Versions.First();
    }

    public ClassDefinition Primary { get; }
    public IReadOnlyList<ClassDefinition> Versions { get; }
    public string Name => Primary.Name;
    public string SourceDisplay => string.Join(", ", Versions.Select(version => version.Source).Distinct());

    public ClassDefinition ResolveVersion(int? selectedClassDefinitionId)
    {
        if (selectedClassDefinitionId is not null)
        {
            var selected = Versions.FirstOrDefault(version => version.Id == selectedClassDefinitionId.Value);
            if (selected is not null)
            {
                return selected;
            }
        }

        return Primary;
    }

    private static int SourceSortKey(string source)
    {
        return source switch
        {
            "PHB" => 0,
            "XPHB" => 1,
            _ => 2
        };
    }
}
