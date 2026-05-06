using DigitalCharacterSheet.Services;

SQLitePCL.Batteries_V2.Init();

var options = ParseArgs(args);
var sourceDataPath = options.SourceDataPath;
var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
    ? Path.Combine("Resources", "Raw", "seed", "digital-character-sheet.db3")
    : options.OutputPath;

while (string.IsNullOrWhiteSpace(sourceDataPath) || !IsValidSourceDataPath(sourceDataPath))
{
    if (!string.IsNullOrWhiteSpace(sourceDataPath))
    {
        Console.WriteLine("The path does not look like a 5e Tools data folder.");
        Console.WriteLine("Expected files/folders include: spells, class, races.json, backgrounds.json, feats.json");
    }

    Console.Write("Path to your local 5e Tools data folder: ");
    sourceDataPath = Console.ReadLine()?.Trim('"', ' ');
}

Console.WriteLine($"Using source data: {Path.GetFullPath(sourceDataPath)}");
Console.WriteLine($"Writing seed database: {Path.GetFullPath(outputPath)}");

await SpellDatabase.CreateSeedDatabaseAsync(outputPath, sourceDataPath);

Console.WriteLine($"Seed database created: {Path.GetFullPath(outputPath)}");

static (string? SourceDataPath, string? OutputPath) ParseArgs(string[] args)
{
    string? sourceDataPath = null;
    string? outputPath = null;

    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        if (argument is "--source" or "-s")
        {
            sourceDataPath = ReadNextArgument(args, ref index, argument);
            continue;
        }

        if (argument is "--output" or "-o")
        {
            outputPath = ReadNextArgument(args, ref index, argument);
            continue;
        }

        if (outputPath is null)
        {
            outputPath = argument;
        }
    }

    return (sourceDataPath, outputPath);
}

static string ReadNextArgument(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"Missing value for {optionName}.");
    }

    index++;
    return args[index];
}

static bool IsValidSourceDataPath(string sourceDataPath)
{
    return Directory.Exists(sourceDataPath)
        && Directory.Exists(Path.Combine(sourceDataPath, "spells"))
        && Directory.Exists(Path.Combine(sourceDataPath, "class"))
        && File.Exists(Path.Combine(sourceDataPath, "races.json"))
        && File.Exists(Path.Combine(sourceDataPath, "backgrounds.json"))
        && File.Exists(Path.Combine(sourceDataPath, "feats.json"));
}
