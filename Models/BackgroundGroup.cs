namespace DigitalCharacterSheet.Models;

public sealed class BackgroundGroup
{
    public BackgroundGroup(IEnumerable<BackgroundDefinition> versions)
    {
        Versions = versions.OrderBy(version => version.Source).ToList();
        Primary = Versions.First();
        Name = Primary.Name;
    }

    public string Name { get; }
    public BackgroundDefinition Primary { get; }
    public IReadOnlyList<BackgroundDefinition> Versions { get; }
    public string SourceDisplay => string.Join(", ", Versions.Select(version => version.Source).Distinct());
}
