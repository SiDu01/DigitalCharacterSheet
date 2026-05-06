# Building Digital Character Sheet

## Recommended Tablet APK

Use the `Tablet` configuration for Android tablets. It is intentionally close to the working Debug build and avoids the Release optimizations that currently cause startup or UI issues on the Lenovo tablet.

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Tablet
```

The signed APK is created here:

```text
bin\Tablet\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk
```

Install it with ADB:

```powershell
& 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe' install --no-incremental -r 'bin\Tablet\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk'
```

## Debug Build

Use Debug for local development and diagnostics:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Debug
```

The signed APK is created here:

```text
bin\Debug\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk
```

## Release Build

The normal `Release` configuration currently builds, but it is not the tested tablet distribution path. On the Lenovo tablet, Release optimizations have caused freezes or startup crashes. Do not use Release for tablet testing until the Release optimization issue has been isolated.

## Seed Database

The bundled seed database lives here:

```text
Resources\Raw\seed\digital-character-sheet.db3
```

Build or refresh it with:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
```

The builder asks for the path to your local `5e Tools\data` folder and writes the seed database to:

```text
Resources\Raw\seed\digital-character-sheet.db3
```

You can also run it non-interactively:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj -- --source "D:\Dev\5e Tools\data" --output Resources\Raw\seed\digital-character-sheet.db3
```

The app copies the seed database only when no app database exists yet. Installing over an existing app keeps existing app data. To force a fresh first-run database copy, clear app data:

```powershell
& 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe' shell pm clear com.companyname.digitalcharactersheet
```

Warning: clearing app data deletes local characters and other user data on the device.

## Database Versioning

The app stores database state in the `DatabaseMetadata` table. Important keys are:

- `DatabaseVersion`: structural app database version.
- `SeedDatabaseVersion`: version label for the bundled seed database.
- `SourceDataVersion`: combined version of imported spell, class, option, and spell-access data.
- `LastInitializedUtc`: last initialization timestamp written by the app.

Future schema changes should add a new `DatabaseVersion` value in `SpellDatabase` and a matching migration in `ApplyMigrationAsync`. Migrations must preserve user-owned tables such as characters, character classes, character spells, spell slots, saving throws, skills, and feats.
