using SQLite;

namespace DigitalCharacterSheet.Data;

[Table("SpellcastingProgressions")]
public sealed class SpellcastingProgressionEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public double MulticlassWeight { get; set; }
    public string RoundingRule { get; set; } = "";
    public bool UsesPactMagic { get; set; }
}
