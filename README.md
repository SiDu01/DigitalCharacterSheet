# Digital Character Sheet

Digital Character Sheet is a .NET MAUI Blazor Hybrid app for Android tablets. The current focus is a D&D character sheet with spells, character creation/editing, class/subclass features, level-up support, items, source filtering, themes, and tablet-friendly views.

The project intentionally does not include the raw `5e Tools` data files. Each developer builds their own local seed database from their own local `5e Tools\data` folder.

## Requirements

- .NET 9 SDK with MAUI workloads
- Visual Studio with MAUI/Android support, or the .NET CLI
- Android SDK platform tools for tablet installation via ADB
- A local checkout/download of `5e Tools` data when building the seed database

## Seed Database

The app expects a local seed database here:

```text
Resources\Raw\seed\digital-character-sheet.db3
```

Create or refresh it with:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
```

The builder asks for your local `5e Tools\data` folder and writes the `.db3` file into the app project's seed folder by default.

The raw JSON files from `5e Tools` are not packaged into the app. Only the seed database is included in local builds.

## Build For PC Testing

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-windows10.0.19041.0
```

You can also start the Windows target from Visual Studio.

## Build And Install On Android Tablet

The tested tablet configuration is `Tablet`:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Tablet
```

The APK is created here:

```text
bin\Tablet\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk
```

Install it with ADB:

```powershell
& 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe' install --no-incremental -r 'bin\Tablet\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk'
```

## Repository Hygiene

Do not commit:

- `Resources\Raw`
- raw `5e Tools` JSON files
- generated `.db3` seed databases
- APK/AAB build outputs
- `bin`, `obj`, `.vs`, or `*.user` files

Detailed build notes live in `BUILDING.md`.
