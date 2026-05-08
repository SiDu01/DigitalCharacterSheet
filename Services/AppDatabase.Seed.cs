using DigitalCharacterSheet.Data;

namespace DigitalCharacterSheet.Services;

public sealed partial class AppDatabase
{
    private static void TryCopySeedDatabase(string databasePath)
    {
#if SEED_BUILDER
        return;
#else
        if (File.Exists(databasePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var seedStream = OpenAssetAsync(SeedDatabaseAssetName).GetAwaiter().GetResult();
            using var databaseStream = File.Create(databasePath);
            seedStream.CopyTo(databaseStream);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"The bundled seed database asset '{SeedDatabaseAssetName}' was not found. " +
                "Build the seed database with Tools\\SeedDatabaseBuilder before packaging or running the app.");
        }
#endif
    }

    private static async Task<Stream> OpenAssetAsync(string assetPath)
    {
#if SEED_BUILDER
        return await Task.FromResult<Stream>(File.OpenRead(Path.Combine(seedSourceDataPath, assetPath)));
#else
        try
        {
            return await FileSystem.OpenAppPackageFileAsync(assetPath);
        }
        catch (FileNotFoundException) when (File.Exists(Path.Combine("Resources", "Raw", assetPath)))
        {
            return File.OpenRead(Path.Combine("Resources", "Raw", assetPath));
        }
#endif
    }
}
