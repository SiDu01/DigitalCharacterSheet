namespace DigitalCharacterSheet.Models;

public sealed class RaceGroup
{
    public RaceGroup(IEnumerable<RaceDefinition> versions)
    {
        Versions = versions.OrderBy(version => version.Source).ToList();
        Primary = Versions.First();
        Name = Primary.Name;
    }

    public string Name { get; }
    public RaceDefinition Primary { get; }
    public IReadOnlyList<RaceDefinition> Versions { get; }
    public string SourceDisplay => string.Join(", ", Versions.Select(version => version.Source).Distinct());
}
