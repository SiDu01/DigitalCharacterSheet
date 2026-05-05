namespace DigitalCharacterSheet.Models;

public sealed class FeatGroup
{
    public FeatGroup(IEnumerable<FeatDefinition> versions)
    {
        Versions = versions.OrderBy(version => version.Source).ToList();
        Primary = Versions.First();
        Name = Primary.Name;
    }

    public string Name { get; }
    public FeatDefinition Primary { get; }
    public IReadOnlyList<FeatDefinition> Versions { get; }
    public string SourceDisplay => string.Join(", ", Versions.Select(version => version.Source).Distinct());
}
