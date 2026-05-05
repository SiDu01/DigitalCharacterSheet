namespace DigitalCharacterSheet.Models;

public sealed class CharacterClass
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int ClassDefinitionId { get; set; }
    public int? SubclassDefinitionId { get; set; }
    public string ClassName { get; set; } = "";
    public string ClassSource { get; set; } = "";
    public string SubclassName { get; set; } = "";
    public string SubclassSource { get; set; } = "";
    public int Level { get; set; }

    public string DisplayName
    {
        get
        {
            var subclass = string.IsNullOrWhiteSpace(SubclassName)
                ? ""
                : $" - {SubclassName} ({SubclassSource})";

            return $"{ClassName} {Level} ({ClassSource}){subclass}";
        }
    }
}
