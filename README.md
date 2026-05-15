# Digital Character Sheet

Digital Character Sheet is an Android-first D&D 5e character tracking app built with .NET 9, .NET MAUI, Blazor Hybrid, and SQLite. The app is designed for tablet play at the table; the Windows target is primarily used for local development and quick UI testing.

The app manages character creation, character editing, the character sheet, spells, combat views, features, items, inventory, level-up, source preferences, themes, bookmarks, recent entries, and import/export.

Raw `5e Tools` JSON is intentionally not shipped in the app. Reference data is imported into a local SQLite seed database during development, and runtime builds use that database as their packaged data source.

## Repository Layout

The MAUI app project lives at the repository root.

```text
.
|-- Components/              Blazor layouts, shared components, and pages
|-- Data/                    SQLite entity types
|-- Models/                  UI/domain models used by pages and services
|-- Services/                Database, rule mapping, settings, imports, activity services
|-- Tools/SeedDatabaseBuilder
|-- Resources/               MAUI assets, icons, fonts, optional seed database
|-- wwwroot/                 Blazor host page and global CSS
|-- Platforms/               MAUI platform projects
|-- docs/                    Architecture and development documentation
```

## Core Commands

Windows build:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-windows10.0.19041.0
```

Android tablet build:

```powershell
dotnet build DigitalCharacterSheet.csproj -f net9.0-android -c Tablet
```

Seed database builder:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
```

## Seed Database

The app expects the generated seed database here when packaging local builds:

```text
Resources\Raw\seed\digital-character-sheet.db3
```

The builder asks for a local `5e Tools\data` folder and writes the `.db3` file into the seed folder. If importer logic, schema, or source JSON mappings change, regenerate the seed database before expecting the app to reflect those changes.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Data and Rules](docs/DATA_AND_RULES.md)
- [Development Workflow](docs/DEVELOPMENT.md)
- [Build Notes](BUILDING.md)
- [Handoff](Handoff.md)

## Repository Hygiene

Do not commit raw `5e Tools` JSON, generated seed databases, APK/AAB outputs, `bin`, `obj`, `.vs`, or `*.user` files.
