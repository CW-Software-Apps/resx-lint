# Product

## Register

product

## Platform

web

## Users

Two audiences, one tool. Primary: .NET/MAUI developers who run `resx-lint` as an MSBuild pre-build step in CI or locally, catching missing translation keys before they ship. Secondary: translation managers and editors who use the self-hosted web UI to view, edit, and batch-translate .resx files side by side. Each audience interacts with a different surface (CLI build output vs. browser dashboard), but both depend on the same quality guarantee.

## Product Purpose

`resx-lint` catches missing, duplicate, and untranslated localization keys before they reach production. It validates every `{maui:Translate Key}` in XAML and every `AppResources.Key` in C# against the real .resx files, auto-fixes what it can, and surfaces the rest as actionable MSBuild errors. The web UI extends this into a visual translation studio — edit cells, batch-translate with AI, see status across all languages at once. Success looks like a CI build that never ships a missing translation, and a translation team that never opens a raw XML file.

## Positioning

Ship confident multi-language apps.

## Brand Personality

Precise, confident, clean. The tool earns trust by being exact about what it finds and transparent about what it changes. No guessing, no surprises. The visual language follows: clear hierarchy, deliberate spacing, no decoration that doesn't carry information.

## Anti-references

- The old .NET options-dialog density — nested group boxes, cramped controls, no visual breathing room
- Over-abstracted "modern" dashboards that hide state behind color codes and icons, forcing the user to hunt for what matters
- Generic SaaS indigo-and-cream templates that look like a startup landing page, not a developer tool

## Design Principles

- **Precision over decoration.** Every element earns its space by carrying information. The tool validates code; the UI should feel equally exact.
- **Show state, don't hide it.** Empty states and zero-counts are as important as errors. A translator needs to see all keys, not just the broken ones. Surface density when it serves the task.
- **Meet both audiences where they are.** The CLI speaks in terse, MSBuild-native errors with file-line references. The web UI speaks in visual matrices with filters, search, and batch actions. Same data, different registers.
- **Confidence through clarity.** Every screen should leave the user certain about their translation state — no doubt, no ambiguity, no second-guessing what "green" means.
- **Stay in the ecosystem.** This is a .NET tool. The design should feel at home next to Visual Studio, Rider, and the .NET CLI — not like a separate web app bolted on.

## Accessibility & Inclusion

Standard web accessibility: dark/light theme already built, keyboard-navigable controls, readable base font sizes. No specific WCAG level target beyond keeping text legible and interactive elements operable.
