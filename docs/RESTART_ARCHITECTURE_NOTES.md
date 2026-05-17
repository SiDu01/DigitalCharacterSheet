# Restart Architecture Notes

This document captures architectural decisions that would be made differently if Digital Character Sheet were started again from scratch. It is not a rewrite plan for the current app. Use it as a north star for future refactors, importer work, and new rule-system features.

## Core Premise

The app should be treated as a local D&D rules and character engine with a wiki surface and character UI on top.

The most important architectural shift would be to design the rule and data layers first, then build creator, edit, level-up, sheet, and wiki flows on top of the same model.

## 1. Separate Raw Data, Rules, Character State, and Sheet Output

The current app already moves in this direction, but a fresh start should make this separation explicit from day one.

Recommended layers:

- Raw Import Model: close to `5e Tools` JSON, disposable, used only by import/build tools.
- Normalized Rules Model: app-owned, structured, queryable rules and options.
- Character State: user-owned choices, references, base values, manual overrides, and inventory state.
- Derived Sheet Model: calculated output for abilities, proficiencies, features, spells, defenses, attacks, and summaries.
- Wiki Model: read-only display and search data, connected to rules where possible but not identical to them.

Primary rule: character data should store user intent, not derived sheet results.

## 2. Treat the Import Pipeline as a Product

The seed database builder should be a first-class project area, not just a helper.

The importer should produce:

- normalized reference data
- wiki entries
- rule definitions
- mapping confidence reports
- unmapped-text reports
- duplicate/source-version reports
- validation output for known difficult examples

Useful reports:

- races that require choices
- feats that grant ability choices
- repeatable feats
- class levels that create feature choices
- skill/tool/language proficiency choices
- subclass grant-level mapping
- text blocks with no supported rule mapping

The goal is not to perfectly understand every source text automatically. The goal is to know what is structured, what is inferred, and what remains wiki-only.

## 3. Build a Generic Choice System

Many current features are variations of the same underlying problem: the app needs to present a constrained choice from a rule source and persist the answer.

A fresh architecture should define one generic choice model.

Important concepts:

- Source: race, race version, background, class, subclass, class feature, feat, item, or manual adjustment.
- Scope: character, class instance, class level, feature instance, item instance, or feat instance.
- Type: ability bonus, skill proficiency, tool proficiency, language, defense, spell, feat, option set, expertise, fighting style, or custom rule option.
- Constraints: allowed options, count, minimum/maximum values, repeatability, prerequisites, and mutual exclusivity.
- Instance key: stable identifier for repeated choices, such as two separate repeatable feats or two ASI grants.
- Selection state: the persisted answer, independent from display text.

Creator, Edit, Level-Up, and future maintenance tools should all consume this same choice engine.

## 4. Share One Rule Evaluation Core

The user experiences can differ, but the rule engine should be shared.

Recommended services:

- CharacterPlan: describes pending changes before they are committed.
- RuleEvaluationService: evaluates open choices, granted effects, derived values, warnings, and missing selections.
- CharacterCommitService: writes accepted changes to persistent character state.
- DerivedSheetService: calculates the display-ready sheet from character state and rules.

This keeps Character Create, Character Edit, Level-Up, and Character Detail aligned while still allowing different UI workflows.

## 5. Use AI as a Mapping Assistant, Not the Source of Truth

Local AI can be useful for converting natural-language `5e Tools` entries into structured rule candidates, but it should not become the only authority.

Preferred approach:

- deterministic importers for known JSON structures
- hand-written parsers for common text patterns
- AI-generated mapping suggestions for difficult prose
- reviewable mapping files checked into the repo
- validation tests for accepted mappings
- confidence scores or explicit mapping status

Good statuses:

- structured
- inferred
- reviewed
- unsupported
- wiki-only

This lets the app benefit from AI-assisted extraction without making runtime behavior mysterious.

## 6. Separate Wiki Entries from Functional Rules

A wiki entry is display/search content. A rule definition is machine-actionable behavior. They may reference each other, but they should not be the same object.

Recommended relationship:

- WikiEntry: name, category, type, source, page, display paragraphs, raw display JSON.
- RuleDefinition: structured effects, choices, conditions, scaling, and prerequisites.
- RuleAttachment: connects a rule definition to a race, class feature, feat, item, or wiki entry.

This allows the wiki to grow quickly without forcing every entry into functional character logic immediately.

## 7. Make Source Versioning a Core Concept

D&D data contains many equal-named entities across sources. A fresh design should treat this as normal, not exceptional.

Recommended concepts:

- Display Group: user-facing identity, such as `Shifter`, `Fighter`, or `Battle Master`.
- Source Version: concrete source-specific definition.
- Child Option: race version, subclass, variant, optional feature, or other nested option.
- User Source Preference: enabled sources and preferred source order.
- Explicit Selection: the source version actually chosen by a character or viewed in the wiki.

Rules:

- source versions should be switchable in lookup UI
- grouped names should stay grouped where that helps browsing
- character selections must preserve the exact source version selected
- source preference should influence defaults, not erase alternatives

## 8. Keep SQLite, but Narrow Its Responsibility

A full restart should still keep SQLite. The app needs local reference data, user-owned character data, searching, filtering, migrations, source settings, bookmarks, recent entries, inventory state, and import/export behavior. Removing the database would likely make the app simpler only for a much smaller wiki-only tool.

SQLite should not become the rule engine. It should be a storage and query layer.

Recommended data flow:

```text
5e Tools JSON
   -> Import/Normalize Tool
   -> SQLite Seed DB
   -> App Query Services
   -> Rule Evaluation Layer
   -> UI
```

Avoid this shape:

```text
SQLite rows
   -> UI pages directly interpreting raw JSON and rule text
```

Useful service boundaries:

- ReferenceDataStore: reads spells, items, wiki entries, classes, races, feats, and source versions.
- CharacterStore: persists user-owned character state.
- WikiService: provides read-only lookup data.
- RuleEvaluationService: calculates choices, effects, warnings, and derived values.
- ChoiceService: creates, validates, and persists rule choices.

SQLite could be skipped only for a much smaller app with no complex character persistence, no migrations, few reference files, minimal filtering, and no offline state beyond simple preferences. That is not the current product shape.

## 9. Prefer an Internal UI System Over a Large UI Framework

A UI framework could help with common controls, but it should not be introduced just to avoid writing components. The app has a specific Android-tablet and leather-journal direction, and many external Blazor frameworks bring strong visual assumptions.

Frameworks worth evaluating if the UI grows hard to maintain:

- MudBlazor: mature and complete, but strongly Material-styled.
- Blazorise: flexible and backend-agnostic, but still needs careful theming.
- Fluent UI Blazor: polished, but more Microsoft/desktop-like.
- Radzen Blazor: broad business-component coverage, but likely needs heavy visual adaptation.

Recommended default:

- do not replace the current UI with a full external framework
- build a small internal component system around the app's real patterns
- keep styling compatible with the default and leather themes
- use external components only for targeted needs after testing them in MAUI Blazor Hybrid

Useful internal components:

- AppButton
- IconButton
- SourceSwitcher
- SegmentedControl
- DefinitionBrowser
- DetailPane
- FeatureLevelList
- ChoicePanel
- StatStepper
- SheetPanel
- ActivityBookmarkButton

The goal is not to invent a framework. The goal is to stop repeating master/detail, source switching, choice, badge, and feature-list logic across pages.

## 10. Test the Difficult Rule Nodes Early

The test suite should focus first on high-risk rule and import examples, not broad UI coverage.

Important regression cases:

- Shifter race versions and their extra choices
- repeatable feats
- Ability Score Improvement from background versus feat
- class levels that grant feats or ASIs above level 1
- subclass features mapped onto class grant levels
- skill proficiency versus tool proficiency choices
- expertise choices
- source switching for same-name spells, races, subclasses, and feats
- granted feats from race/background
- multiclass requirements

These tests should run against importer output and rule evaluation, not only UI components.

## 11. Build Reusable Browse and Detail Components Earlier

The app has several recurring surfaces:

- spells browser
- items browser
- wiki/library browser
- creator option selection
- inventory add flow
- source switching
- feature level display
- reference detail panes

A fresh implementation should build these as reusable UI patterns earlier:

- DefinitionBrowser
- SourceSwitcher
- ChoicePanel
- FeatureLevelList
- ReferenceDetailPane
- ActivityBookmarkButton

These should stay pragmatic components, not a large generic framework.

## 12. Design Android Tablet Layouts First

Android tablet should drive the default layout decisions.

Baseline expectations:

- touch-friendly controls
- stable scroll zones
- master/detail layouts where browsing matters
- compact but readable panels
- no nested scrolling unless unavoidable
- no desktop-only assumptions for hover or width
- visual testing on Windows plus regular Android tablet builds

Windows remains useful for development, but it should not define the final UX.

## Practical Refactor Direction for the Current App

Do not restart the project solely to pursue this architecture. The current codebase is useful and can evolve toward these ideas.

Highest-value incremental moves:

1. Create a more explicit rule evaluation service shared by Create, Edit, Level-Up, and Sheet.
2. Consolidate ability, defense, proficiency, feat, and feature choices into a generic choice model.
3. Strengthen importer reports and add validation examples for known hard cases.
4. Keep expanding the Library/Wiki as display data while attaching functional rules only when structured.
5. Move item effects into the same dynamic effect pipeline as race, class, background, and feat effects.
6. Introduce internal shared UI components for source switching, definition browsing, feature levels, and choice panels.
7. Add targeted regression tests around source grouping, repeatable choices, and subclass feature mapping.

## Decision Summary

If started again, the fundamental decision would be:

Build a rules/import architecture first, keep SQLite as the storage/query layer, and place the character workflows and wiki UI on top of that.

The current app should continue in that direction without throwing away the working MAUI/Blazor shell, SQLite persistence, custom visual design, or existing character workflows.
