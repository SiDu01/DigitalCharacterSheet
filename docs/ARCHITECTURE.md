# Architecture

Digital Character Sheet is a single-project .NET MAUI Blazor Hybrid app. MAUI hosts the native shell and platform packaging, while the user interface is built with Razor components rendered in a Blazor WebView.

## Goals

- Android tablet first.
- Windows target for local development and fast smoke testing.
- Local-first data model with SQLite.
- No raw `5e Tools` JSON in runtime packages.
- Store character inputs, references, and choices; calculate derived values from current definitions whenever possible.

## Application Layers

### MAUI Shell

Relevant files:

- `MauiProgram.cs`
- `App.xaml`
- `MainPage.xaml`
- `Platforms/*`

`MauiProgram` initializes SQLitePCL, configures MAUI/Blazor WebView, registers services, and enables developer tooling in debug builds.

Registered services:

- `SpellImportService`
- `AppDatabase`
- `TextBadgeSettingsService`
- `RecentActivityService`

### Blazor UI

Relevant folders:

- `Components/Layout`
- `Components/Pages`
- `Components/*.razor`
- `wwwroot/css/app.css`

Main pages:

- `Home.razor`
- `Characters.razor`
- `CharacterCreate.razor`
- `CharacterEdit.razor`
- `CharacterDetail.razor`
- `CharacterLevelUp.razor`
- `Spells.razor`
- `SpellDetail.razor`
- `Items.razor`
- `Settings.razor`

The app favors tablet-friendly split layouts: list/detail surfaces, dense but readable sheet panels, and full-screen browse/add flows where a modal would be too cramped.

### Services

Relevant folder:

- `Services`

`AppDatabase` is the main persistence and reference-data service. It is split across partial classes by responsibility:

- `AppDatabase.cs`: initialization, table creation, shared helpers, export model types.
- `AppDatabase.Migrations.cs`: database versioning and migrations.
- `AppDatabase.Schema.cs`: compatibility columns and schema helpers.
- `AppDatabase.Definitions.cs`: class, subclass, race, background, feat, source option, and rule-effect definition logic.
- `AppDatabase.Spells.cs`: spell import, spell access, and spell queries.
- `AppDatabase.Items.cs`: item import, generated magic variants, item queries.
- `AppDatabase.Inventory.cs`: character inventory.
- `AppDatabase.Characters.cs`: character CRUD, import/export, spells, slots, features, granted effects, proficiencies.
- `AppDatabase.Seed.cs`: seed database copy behavior.
- `AppDatabase.Sources.cs`: source settings.

Other important services:

- `CharacterAbilityChoiceService`: parses and stores ability-choice requirements and selected ability bonuses.
- `CharacterDefenseChoiceService`: parses and stores defense-choice requirements.
- `DescriptionRuleMapper`: maps natural-language feature text into supported rule/effect hints.
- `ItemEffectService`: derives supported item effects.
- `RecentActivityService`: local bookmarks and recent activity.
- `TextBadgeSettingsService`: local text badge preferences.

### Models and Data Entities

Relevant folders:

- `Models`
- `Data`

`Data` types map directly to SQLite tables. `Models` are app-facing types consumed by pages and services. Keep this split intact: add persistence fields to `Data`, then map them into richer `Models` only when they are needed by UI or rule logic.

## Runtime Data Flow

1. App starts and requests `AppDatabase`.
2. `AppDatabase` copies the bundled seed database into app data if no app database exists.
3. Tables are created idempotently.
4. Migrations are applied based on `DatabaseMetadata.DatabaseVersion`.
5. Source data versions are validated against the app's expected import versions.
6. UI pages query definitions and character data through `AppDatabase`.
7. Character sheet and editor surfaces combine stored character state with reference definitions and choice JSON.

## Important Design Rules

- Keep UI text in English.
- Prefer dynamic calculation over storing stale derived values.
- Persist manual user selections.
- Do not restrict subclass source by class source unless a specific rule requires it.
- Group entities with the same name and allow source switching where relevant.
- If one source version of a class grants spell access, all source versions of that spell may remain selectable.
- Keep Android/tablet usability in mind before optimizing for desktop.
- Use existing patterns before adding new abstractions.

## Character Edit Philosophy

`CharacterCreate` is a guided creation flow. `CharacterEdit` is a free-form maintenance surface for an existing character.

The editor should let the user change all stored inputs:

- identity
- race/background references
- classes, levels, subclasses
- base ability scores
- feats
- rule choices
- manual proficiencies

It should not treat calculated sheet values as canonical character state. The canonical state is the combination of:

- selected references
- base values
- selected choices
- manual overrides where they intentionally exist

Derived values should be resolved from those inputs when the character is displayed or recalculated.

## Styling

Global styling lives in `wwwroot/css/app.css`. The app currently has the default theme and the leather journal theme. When adding a new component surface, add theme-specific styles for `[data-theme="leather"]` at the same time if the component has custom backgrounds, borders, or text colors.
