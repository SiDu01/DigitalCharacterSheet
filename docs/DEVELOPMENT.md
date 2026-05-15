# Development Workflow

This document captures the normal development loop for Digital Character Sheet.

## Prerequisites

- .NET 9 SDK
- .NET MAUI workloads
- Android SDK platform tools for tablet installs
- Optional: Visual Studio with MAUI and Android support
- Local `5e Tools\data` folder when rebuilding the seed database

## First Setup

From the repository root:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-windows10.0.19041.0
```

If reference data is missing, build the seed database:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
```

The builder prompts for the local `5e Tools\data` folder and writes:

```text
Resources\Raw\seed\digital-character-sheet.db3
```

## Common Builds

Windows development build:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-windows10.0.19041.0
```

Android tablet build:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Tablet
```

The Android build may show the known `AndroidFastDeploymentType` warning. It is not currently treated as a blocker.

## Tablet Install

Build first:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Tablet
```

Install:

```powershell
& 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe' install --no-incremental -r 'bin\Tablet\net9.0-android\com.companyname.digitalcharactersheet-Signed.apk'
```

Clear app data when a fresh first-run seed copy is needed:

```powershell
& 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe' shell pm clear com.companyname.digitalcharactersheet
```

Warning: clearing app data deletes local characters and settings on the device.

## Testing Checklist

For meaningful feature changes:

- Build Windows target.
- Build Android Tablet target.
- Smoke test the touched workflow.
- If creator/edit logic changed, smoke test both Character Create and Character Edit.
- If class/feat/effect logic changed, smoke test Level-Up and the Character Sheet.
- If data/importer logic changed, regenerate the seed database and verify startup.
- If tablet-specific layout changed, install and test on tablet.

## UI Guidelines

- Keep UI text in English.
- Favor tablet-first density and readability.
- Avoid large layout rewrites of character sheet surfaces unless visually tested.
- When adding custom surfaces, verify the default theme and leather journal theme.
- Keep controls stable in size, especially repeated rows and compact editors.
- Prefer source switching and grouped definitions where duplicate names exist.

## Character Workflows

Character creation is a guided step flow.

Character editing is a free-form maintenance flow. It should allow broad changes while keeping the stored-data model clean:

- store base inputs and references
- store choices
- preserve manual selections
- recalculate derived effects from active references

## Data Change Workflow

When changing schema:

1. Update `DatabaseVersion`.
2. Add a migration in `AppDatabase.Migrations.cs`.
3. Keep user-owned data intact.
4. Build Windows and Android Tablet.

When changing importers or source mappings:

1. Update the relevant import version.
2. Regenerate `Resources\Raw\seed\digital-character-sheet.db3`.
3. Confirm startup on a clean app database.
4. Build Windows and Android Tablet.

## Git Hygiene

Do not commit:

- `bin`
- `obj`
- `.vs`
- `*.user`
- raw `5e Tools` JSON
- generated seed databases
- APK/AAB outputs

Before committing, review:

```powershell
git status --short
git diff
```
