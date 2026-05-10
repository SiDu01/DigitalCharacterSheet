namespace DigitalCharacterSheet.Models;

public sealed class Character
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? RaceDefinitionId { get; set; }
    public int? SubraceDefinitionId { get; set; }
    public string RaceName { get; set; } = "";
    public string RaceSource { get; set; } = "";
    public string SubraceName { get; set; } = "";
    public string SubraceSource { get; set; } = "";
    public string RaceChoicesJson { get; set; } = "";
    public int? BackgroundDefinitionId { get; set; }
    public string BackgroundName { get; set; } = "";
    public string BackgroundSource { get; set; } = "";
    public string BackgroundChoicesJson { get; set; } = "";
    public string FeatChoicesJson { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int? PrimaryClassDefinitionId { get; set; }
    public int? Level { get; set; }
    public int Strength { get; set; } = 10;
    public int Dexterity { get; set; } = 10;
    public int Constitution { get; set; } = 10;
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;
    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int TemporaryHitPoints { get; set; }
    public string ConditionsJson { get; set; } = "";
    public List<CharacterClass> Classes { get; set; } = [];
    public List<CharacterFeat> Feats { get; set; } = [];
    public List<CharacterSavingThrow> SavingThrows { get; set; } = CharacterSavingThrow.CreateDefaults();
    public List<CharacterSkill> Skills { get; set; } = CharacterSkill.CreateDefaults();
    public List<CharacterGrantedEffect> GrantedEffects { get; set; } = [];
    public List<CharacterFightingStyle> FightingStyles { get; set; } = [];
    public List<CharacterToolProficiency> ToolProficiencies { get; set; } = CharacterToolProficiency.CreateDefaults();
    public List<CharacterLanguageProficiency> LanguageProficiencies { get; set; } = CharacterLanguageProficiency.CreateDefaults();

    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Classes.Count > 0)
            {
                parts.Add(string.Join(" / ", Classes.Select(characterClass => characterClass.DisplayName)));
            }
            else if (!string.IsNullOrWhiteSpace(ClassName))
            {
                parts.Add(ClassName);
            }

            if (Level is not null)
            {
                parts.Add($"Level {Level}");
            }

            return parts.Count == 0 ? "No class set" : string.Join(" - ", parts);
        }
    }
}
