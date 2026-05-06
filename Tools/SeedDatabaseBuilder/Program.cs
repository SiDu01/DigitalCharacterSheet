using DigitalCharacterSheet.Services;

SQLitePCL.Batteries_V2.Init();

const string SeedDatabaseFileName = "digital-character-sheet.db3";

var options = ParseArgs(args);
var sourceDataPath = options.SourceDataPath;
var projectRoot = FindProjectRoot();
var defaultOutputDirectory = Path.Combine(projectRoot, "Resources", "Raw", "seed");
var outputDirectory = string.IsNullOrWhiteSpace(options.OutputPath)
    ? defaultOutputDirectory
    : NormalizeOutputDirectory(options.OutputPath);
var outputPath = Path.Combine(outputDirectory, SeedDatabaseFileName);

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

if (string.IsNullOrWhiteSpace(options.OutputPath))
{
    Console.WriteLine($"Default output folder: {Path.GetFullPath(defaultOutputDirectory)}");
    Console.WriteLine($"Seed database file: {SeedDatabaseFileName}");
    if (!AskYesNo("Use this output folder? [y/n]: "))
    {
        outputDirectory = ReadOutputDirectory();
        outputPath = Path.Combine(outputDirectory, SeedDatabaseFileName);
    }
}

Console.WriteLine($"Using source data: {Path.GetFullPath(sourceDataPath)}");
Console.WriteLine($"Writing seed database to: {Path.GetFullPath(outputPath)}");

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

static string FindProjectRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DigitalCharacterSheet.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DigitalCharacterSheet.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Environment.CurrentDirectory;
}

static string NormalizeOutputDirectory(string outputArgument)
{
    var trimmed = outputArgument.Trim('"', ' ');
    return string.Equals(Path.GetExtension(trimmed), ".db3", StringComparison.OrdinalIgnoreCase)
        ? Path.GetDirectoryName(trimmed) ?? Environment.CurrentDirectory
        : trimmed;
}

static bool AskYesNo(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer is "y" or "yes" or "j" or "ja")
        {
            return true;
        }

        if (answer is "n" or "no" or "nein")
        {
            return false;
        }

        Console.WriteLine("Please answer y or n.");
    }
}

static string ReadOutputDirectory()
{
    while (true)
    {
        Console.Write("Output folder for the seed database: ");
        var outputDirectory = Console.ReadLine()?.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            Console.WriteLine("Please enter an output folder.");
            continue;
        }

        if (string.Equals(Path.GetExtension(outputDirectory), ".db3", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Please enter a folder, not a .db3 file path.");
            continue;
        }

        return outputDirectory;
    }
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
