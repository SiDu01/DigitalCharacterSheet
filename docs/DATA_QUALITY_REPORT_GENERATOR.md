# Data Quality Report Generator

`DataQualityReportGenerator` lives in `Tools\SeedDatabaseBuilder\DataQualityReportGenerator.cs`. It is part of the seed database tooling and scans raw 5e Tools JSON for data shapes and natural-language rule text that are not yet represented as structured app rules.

The generator does not change app data and does not write to the seed database. Its job is to create a parser backlog: concrete source locations, grouped by rule pattern, so unsupported cases can be implemented step by step.

## How To Run

From the repository root:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj -- --reports-only --source "D:\Dev\Digital Character Sheet\external Resources\5e Tools\data"
```

A normal seed build also generates reports after importing data:

```powershell
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
```

Report output:

```text
Tools\SeedDatabaseBuilder\reports\data-quality-report.md
Tools\SeedDatabaseBuilder\reports\unhandled-cases.json
Tools\SeedDatabaseBuilder\reports\unhandled-cases.md
```

The report files are generated artifacts. They are useful for review, but the important source change is usually the generator logic itself.

## Inputs

The generator currently scans these 5e Tools data areas:

- `races.json`: `race`, `subrace` as `race-version`
- `backgrounds.json`: `background`
- `feats.json`: `feat`
- `items.json`: `item`, `itemGroup`, `baseitem`
- `class\*.json`: `class`, `subclass`

Each entity is counted, tracked for duplicate `(category, name, source)` combinations, checked for required shape, and recursively scanned for relevant text.

## Output Model

Each reported item is an `UnhandledCase`:

- `Category`: normalized app-facing category, such as `race`, `feat`, `item`, `class`
- `Name`: source entity name
- `Source`: source book/code
- `Path`: source JSON path relative to the input folder
- `CaseType`: stable bucket name, such as `feat-spell-choice-candidate`
- `Severity`: `error`, `warning`, `unhandled`, `candidate`, or `wiki-only`
- `Confidence`: optional classifier confidence
- `Reason`: human-readable explanation
- `TextPreview`: shortened text sample for text-derived cases
- `SuggestedParser`: intended parser/mapper name

`unhandled-cases.json` is the machine-readable form for tooling. The Markdown files are optimized for review and prioritization.

## Report Sections

`data-quality-report.md` contains:

- entity counts by category
- case counts by severity and case type
- suggested parser backlog, sorted by priority
- detailed case-type sections with category counts and representative examples

`unhandled-cases.md` contains the individual cases in review order.

Priority is calculated from severity, confidence, frequency, category spread, and specific case-type weights in `CalculateParserPriority`.

## Scan Flow

The main entry point is:

```csharp
DataQualityReportGenerator.WriteReports(sourceDataPath, reportDirectory)
```

High-level flow:

1. `AnalyzeRootFile` and `AnalyzeJsonFile` load configured source files and arrays.
2. `AnalyzeEntityShape` checks required entity shape, such as names, sources, race-version parent links, subclass parent links, and class feature arrays.
3. `ScanElement` recursively walks JSON objects, arrays, and strings.
4. `AnalyzeObject` reports unknown entry `type` values.
5. `AnalyzeText` runs text detectors and sends broad matches through more specific classifiers.
6. `FinalizeDuplicateChecks` reports duplicate source-version entries and Foundry overlay duplicates.
7. Markdown and JSON reports are written.

## Text Classification

Text detection starts with broad detectors:

- choice text
- proficiency and expertise text
- defense, resistance, immunity, and vulnerability text
- ability score text
- spell and spellcasting text

Broad matches are refined by classifiers:

- `ClassifyChoiceText`
- `ClassifySpellChoiceText`
- `ClassifyProficiencyText`
- `ClassifyDefenseText`
- `ClassifyProficiencyBonusScalingText`
- `ClassifySpellText`
- item-specific spell activation helpers

This two-step approach keeps broad regexes easy to reason about while allowing precise backlog buckets.

## Current Candidate Families

The generator currently distinguishes these major families:

- missing or malformed source data
- duplicate source versions and Foundry overlays
- choice-like text
- ability score rules
- proficiency, expertise, and proficiency-bonus scaling rules
- defense, resistance, immunity, vulnerability, and condition-defense rules
- spell choices, spell grants, spell lists, spell access, spellcasting ability, spell slots, spellbooks, spell modifiers, spell references, and item spell activations
- item spell activation subtypes such as charge, action, passive, recharge, ritual, spell list, direct spell, source-qualified table cell, and recharge timing

Recent spell-report splits include:

- item spell table cells split into row, list, source-qualified, single, multi, and qualified cases
- item recharge spell activations split into dawn, long-rest, short-or-long-rest, roll recharge, once-per-day, direct, list, and reference cases
- spell modifiers split into damage, save DC, attack, components, concentration, range/duration, healing, and generic modifier cases
- innate spell grants split into ancestry, feat, and item cases
- item spell choices split from generic spell choices

## Adding A New Candidate

When adding a candidate:

1. Add or refine a `[GeneratedRegex]` near the related regex family.
2. Route broad detector matches through a classifier instead of adding a one-off detector when the rule belongs to an existing family.
3. Add a stable `CaseType`, reason, confidence, and `SuggestedParser`.
4. Add the case type to `CalculateParserPriority` when it should be prioritized.
5. Add the case type to `CaseTypeRank` so report ordering stays predictable.
6. Add it to `GetSemanticKey` if it should be grouped with an existing semantic family.
7. Run the report and inspect representative examples before committing.

Recommended verification:

```powershell
dotnet build Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj
dotnet run --project Tools\SeedDatabaseBuilder\SeedDatabaseBuilder.csproj -- --reports-only --source "D:\Dev\Digital Character Sheet\external Resources\5e Tools\data"
git diff --check
```

## Important Boundaries

- The generator identifies parser work; it is not the parser implementation itself.
- It should prefer specific, reviewable candidates over silently ignoring text.
- It should avoid overfitting one named entity when a broader pattern exists.
- It can produce false positives. Use `wiki-only`, flavor/reference buckets, or lower confidence when text is descriptive rather than mechanical.
- Generated reports help decide what to implement next, but app behavior only changes when importers, mappers, services, or UI consume structured results.

## Next Useful Work

Good follow-up areas are:

- continue reducing high-volume item spell activation candidates into actionable parser groups
- turn mature report candidates into real importer/mapping logic
- add tests around representative source snippets once a candidate becomes a parser
- keep comparing generated candidate counts before and after parser work so progress is visible
