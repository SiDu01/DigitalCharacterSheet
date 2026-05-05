using DigitalCharacterSheet.Services;

SQLitePCL.Batteries_V2.Init();

var outputPath = args.Length > 0
    ? args[0]
    : Path.Combine("Resources", "Raw", "seed", "digital-character-sheet.db3");

await SpellDatabase.CreateSeedDatabaseAsync(outputPath);

Console.WriteLine($"Seed database created: {Path.GetFullPath(outputPath)}");
