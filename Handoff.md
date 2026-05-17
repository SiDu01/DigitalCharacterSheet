# Digital Character Sheet - Handoff

## Project Goal

Digital Character Sheet is an Android-first D&D 5e character tracking app. The app is built for tablet use, with Windows builds mainly used for local testing.

The current scope includes:

- Character creation and editing
- Character sheet with Main, Spells, Combat, Features, Items, and Proficiencies areas
- Spell browser and character spell lists
- Item browser and character inventory
- Read-only wiki/library areas for conditions, rules, feats, races, and classes
- Level-up workflow
- Local settings, themes, bookmarks, and recent entries
- Import/export support for characters

The app uses a local SQLite database. Raw 5e Tools JSON is not shipped as the main runtime data source. Instead, a seed database is generated from the user's local 5e Tools checkout.

## Tech Stack

- .NET 9
- .NET MAUI Blazor Hybrid
- SQLite
- Android Tablet build configuration
- Windows target for local testing

UI text should stay in English.

## Important Paths

- App project: `D:\Dev\Digital Character Sheet\DigitalCharacterSheet.csproj`
- Main app folder / repository root: `D:\Dev\Digital Character Sheet`
- Seed database builder: `D:\Dev\Digital Character Sheet\Tools\SeedDatabaseBuilder`
- Seed database output: `D:\Dev\Digital Character Sheet\Resources\Raw\seed\digital-character-sheet.db3`
- Local 5e Tools checkout/data folder: `D:\Dev\Digital Character Sheet\external Resources\5e Tools\data`

Important app files:

- `Components\Pages\Home.razor`
- `Components\Pages\Library.razor`
- `Components\Pages\CharacterCreate.razor`
- `Components\Pages\CharacterEdit.razor`
- `Components\Pages\CharacterDetail.razor`
- `Components\Pages\CharacterLevelUp.razor`
- `Components\Pages\Spells.razor`
- `Components\Pages\Items.razor`
- `Components\Pages\Settings.razor`
- `Services\AppDatabase*.cs`
- `Services\CharacterAbilityChoiceService.cs`
- `Services\CharacterDefenseChoiceService.cs`
- `Services\DescriptionRuleMapper.cs`
- `wwwroot\css\app.css`

## Build Commands

Run from:

```powershell
D:\Dev\Digital Character Sheet
```

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

The Android build usually shows a known warning about `AndroidFastDeploymentType`. This has not been treated as a blocker.

## Runtime Data

The app uses a prebuilt SQLite seed database instead of importing all JSON at runtime.

The seed database is created by `Tools\SeedDatabaseBuilder`. The builder asks for the local 5e Tools data path and writes a `.db3` into the app's seed folder.

Important principle:

- If importer logic, schema, or source JSON mappings change, regenerate the seed database before expecting the app to reflect those changes.

## Data Model Overview

Definition data includes:

- Spells
- Items
- Wiki entries
- Classes
- Subclasses
- Races / ancestries
- Backgrounds
- Feats
- Sources

Character data includes:

- Character base data
- Classes and subclasses
- Feats
- Spells and prepared/known state
- Spell slots
- Inventory
- Abilities
- Saving throws
- Skills
- Tool and language proficiencies
- Granted effects
- Choice JSON fields for selected options

Choice JSON is used for selections that come from rules, such as ability choices, defense choices, race/background choices, and granted feat toggles.

## Architecture Rules

- Android/tablet-first UX.
- Keep UI labels in English.
- Group entities with the same name and allow source switching where relevant.
- Do not restrict subclass source by class source unless explicitly required.
- If one class source grants spell access, all source versions of that spell should remain selectable.
- Prefer dynamic calculation from current class/race/background/feat/item state over storing stale derived values.
- Manual user selections must be persisted.
- Avoid reverting user changes or unrelated worktree changes.
- Use existing app patterns before adding new abstractions.

## Current Feature State

### Home

The home page has tiles, bookmarks, and recently viewed entries. There is no dashboard tile for Settings; settings remain available through navigation. Items and wiki entries from bookmarks/recent entries should deep-link to their specific detail route, not just the category page.

### Spells

General spell list and character spell lists exist. Spells with the same name are grouped, with source switching. Spell list filtering includes class spell lists. Character spell lists have known/prepared distinctions and spell-level separators.

### Items

General item list exists with categories and search. Magic item variants are grouped to avoid huge duplicate lists. Character inventory exists, with add-item flow using a full-screen item list modeled after the general item browser.

Item effects are partially supported and should continue moving toward dynamic effects on the character.

### Library / Wiki

The Library is a read-only lookup area backed by the `WikiEntries` table. It currently includes:

- Conditions and diseases
- Actions and variant rules
- Feats
- Races and race versions
- Classes and subclasses

The Library is organized as separate category areas rather than one large combined list. Within a category, primary records are listed on the left and details are shown on the right.

Important UX rules:

- Subclasses are not shown as independent list records. They appear as activatable badges inside their parent class detail.
- Race versions are not shown as independent list records. They appear as activatable badges inside their parent race detail.
- Multiple subclass/race-version badges can be active at the same time.
- If equal-named subclasses or race versions exist from multiple sources, they are grouped under one badge and the source is selected inside the detail.
- Class descriptions are split by level.
- Active subclass features are inserted into the class level list at the level where the class grants subclass features.
- Library entries can be bookmarked and tracked as recently viewed, but bookmarks and recent entries are displayed on the Dashboard only, not inside the Library categories.

### Character Creator

The creator is step-based:

1. Identity
2. Ancestry
3. Background
4. Classes
5. Feats
6. Abilities
7. Proficiencies
8. Review

The creator uses a left navigation/selection area and a right detail area. Proficiency editing is now in the right detail panel. Review details are also shown on the right.

Ability score modes include:

- Manual
- Standard Array
- Point Buy
- Roll

Ability choice modes include `+2 & +1`. Point Buy uses the base score before race/background/feat bonuses for cost calculation.

Granted feats from ancestry/background can be toggled with `Apply granted feats`. Manual feat filtering has `Show only Origin Feats`, where Origin Feats are identified by `category == "O"`.

### Character Edit

Character Edit is a free-form maintenance surface, not a direct mirror of Character Create. It is organized into sections for Overview, Identity, Classes, Abilities, Choices, Feats, and Proficiencies. The Classes section uses editable class cards with compact level editing and direct subclass selection.

Important principle: Character Edit should store base values, references, choices, and manual selections. Derived sheet values should be recalculated from active class/race/background/feat/item state rather than written back as canonical character data.

### Character Sheet

The sheet has tabs including Main, Spells, Combat, Features, Items, and Proficiencies. Features support hiding user-selected features. Main layout has been iterated many times; avoid large layout rewrites unless testing visually.

### Level-Up

Level-Up can increase an existing class or add a new class if multiclass requirements are met. It handles feat choices, fighting styles, expertise, skill/tool/language choices, and half-proficiency in some cases.

Recent bug fixed:

- `Feature` was incorrectly detected as `Feat` because the code checked `Contains("Feat")`. This caused false Feat choices on levels with `Subclass Feature`. The check now only detects a real `Feat` word, plus explicit `Ability Score Improvement` and `Epic Boon`.

### Settings

Settings uses tabs. Theme switching exists between the default theme and the leather journal theme. Source activation/preference settings exist.

## Important Recent Changes

- Character Creator was rebuilt into real steps.
- Ancestry/background selection uses lists instead of dropdown-only selection.
- Source selection for ancestry/background/classes/feats moved into the detail pane.
- Ability choices now support mode selection, including `+2 & +1`.
- Ability selections now persist and apply correctly after fixing a Razor loop index capture bug in `AbilityChoiceList.razor`.
- Point Buy now calculates cost from base ability values before bonuses.
- Proficiency selection moved from the left panel into the right detail pane.
- Review detail summary now includes abilities, saves, skills, tools, languages, ability choices, and defense choices.
- Granted feats can be toggled in the Feats step.
- Origin Feat filter uses `category == "O"`.
- Level-Up Feat detection no longer treats `Feature` as `Feat`.
- Repository structure was flattened so the MAUI project now lives at `D:\Dev\Digital Character Sheet`.
- Character Edit was rebuilt into a free-form edit center with section navigation, an overview, reference pane, class cards, and direct subclass editing.
- Developer documentation was added under `docs\`.
- A read-only Library/Wiki was added for Conditions, Rules, Feats, Races, and Classes.
- Library categories were separated into their own areas instead of one combined list.
- Subclasses and race versions were moved into their parent detail views as activatable badges.
- Equal-named subclass/race-version entries from different sources are grouped with source switching.
- Class feature detail in the Library is split by level and active subclass features are merged into the relevant class levels.
- Library bookmarks and recently viewed entries are tracked for the Dashboard only.
- The Dashboard Settings tile was removed.

## Known Issues / Watchlist

- Character Create and Character Edit intentionally have different UX. Shared rule behavior should stay aligned, but Edit should remain a broader maintenance surface.
- `Apply granted feats` is currently one broad toggle, not per-source/per-feat persistence.
- Effect parsing from natural-language feature text is incomplete.
- Some class/subclass/race/background/feat effects may still not map to dynamic effects.
- Item effects should be further unified with the broader GrantedEffects/effect-calculation system.
- Some level-up choices may still need better rule-specific handling.
- Seed database must be regenerated after importer/schema changes.
- Release/tablet builds historically had startup and database-loading issues; keep testing Android after data/runtime changes.

## Testing Checklist

Before handing work back after meaningful changes:

- Build Windows target.
- Build Android Tablet target.
- Smoke test Character Creator.
- Smoke test Character Edit if creator-related logic changed.
- Smoke test Level-Up if class/feat/effect logic changed.
- Open general Spells and Items lists.
- Open Library categories, especially Classes and Races, after wiki changes.
- If database/importer changed, rebuild seed database and verify app startup.
- If tablet-specific behavior changed, install and test on tablet.

## Useful Investigation Tips

- Prefer `rg` / `rg --files` for searches.
- Check raw source examples in `D:\Dev\Digital Character Sheet\external Resources\5e Tools\data`.
- For class level features, inspect `data\class\class-*.json`.
- For feat category checks, use the imported `FeatDefinition.Category`; Origin Feats use category `O`.
- If runtime behavior seems inconsistent with JSON, check whether the seed database was regenerated.

## Suggested Next Steps

1. Align Character Edit with the current Character Creator structure where it makes sense.
2. Improve granted feat handling from a global toggle to per granted feat/source toggles.
3. Continue hardening Level-Up choice detection and rule application.
4. Unify item effects with the dynamic character effect system.
5. Expand parser/mapper coverage for class, subclass, race, background, feat, and item descriptions.
6. Add clearer UI indicators for inferred/mapped effects and whether they are user-adjusted.
7. Polish Combat tab information density and action detail behavior.
8. Keep Android tablet install tests as part of larger changes.
