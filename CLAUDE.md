# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`resx-lint` is a .NET 10 global tool (`dotnet tool`, command name `resx-lint`) that validates `.resx` localization keys against XAML (`{maui:Translate Key}`) and C# (`AppResources.Key`) usage in MAUI/other projects. It auto-fixes duplicate keys (including case-only duplicates — `TipoDeServico` vs `TipoDeServiCO`, TRANS009), missing `Designer.cs` properties, and orphaned language entries, and ships a self-hosted web UI (ASP.NET Core + SignalR) as a visual translation editor. Two extra CLI subcommands, `add-key` and `remove-key`, exist specifically so an AI agent (or a human) can add/remove a translation key across the base `.resx` + every language `.resx` + `Designer.cs` in one call instead of hand-editing XML — see README.md → "For AI Agents". It is published to NuGet and consumed as an MSBuild pre-build step in downstream repos.

The whole tool is a single project: `src/ResxLint/ResxLint.csproj` (SDK: `Microsoft.NET.Sdk.Web`, `net10.0`, packed as a dotnet tool via `PackAsTool`).

## Common commands

```bash
# Build
dotnet build src/ResxLint/ResxLint.csproj

# Run from source (equivalent to running the installed tool)
dotnet run --project src/ResxLint/ResxLint.csproj -- --help
dotnet run --project src/ResxLint/ResxLint.csproj -- --project-dir <dir> --resx-file <path.resx>
dotnet run --project src/ResxLint/ResxLint.csproj -- --serve   # launch web UI

# Pack the NuGet tool package (CI does this on tag push)
dotnet pack src/ResxLint/ResxLint.csproj --configuration Release --no-build -o ./nupkg
```

There is no separate test project in this repo — CI's only automated check is `dotnet build` + a self-test invocation of `--help` (see `.github/workflows/ci.yml`). When changing lint logic, validate manually against a real `.resx`/XAML/C# fixture using `--what-if` first.

Publishing to NuGet happens automatically via `.github/workflows/publish.yml` when a `v*` tag is pushed (requires `NUGET_API_KEY` secret). Bump `<Version>` in `ResxLint.csproj` before tagging.

## Architecture

Everything lives under `src/ResxLint/`:

- **`Program.cs`** — CLI entry point and argument parsing (`CliArgs`). Dispatches to one of several modes based on `args[0]`: `--serve`/`-s` (web UI), `--auto-update`, `--check-update`/`-u`, `--help`/`-h`, `add-key`/`remove-key` (see `Services/AddKeyService.cs`/`Services/RemoveKeyService.cs`), or default lint mode. With zero args it shows an interactive 3-second prompt (press `W` for web UI) before falling back to auto-detecting a `.resx` in the CWD. Exit codes: `0` OK, `1` auto-fixes applied (caller should rebuild), `2` invalid params (or `add-key`/`remove-key` refused), `3` fatal errors.

- **`Services/LintService.cs`** — the core engine, run as a fixed 7-step pipeline (`Step1_Duplicates` … `Step6_DesignerCs`, with `Step1b_CaseInsensitiveDuplicates` between 1 and 2) inside `Run()`:
  1. Find/remove duplicate `<data>` keys across all `.resx` files (regex-based, not full XML parsing, for speed).
  1b. Find keys in the base `.resx` that differ **only in casing** (`TipoDeServico` vs `TipoDeServiCO` — `TRANS009`). Runs a lightweight usage scan (same regexes as steps 4/5) to decide: exactly one variant referenced → auto-remove the other; neither referenced → warn, don't guess; **both** referenced → fatal, needs a human to merge the usages first. This is the step `add-key`/`remove-key` exist to make unnecessary to ever trigger from an AI edit — see README.md → "For AI Agents".
  2. Load the base `.resx` (`LoadResxData`, XML-based) and flag placeholder/empty values (`TRANS007`).
  3. Cross-check every `AppResources.*.resx` language file against the base: keys only in a language file get backfilled into the base with a `[TRANSLATE: ...]` placeholder (`TRANS005`); keys missing from a language file are warned (`TRANS006`). There is deliberately no "value identical to base" check (`TRANS008` was removed, and that number is retired — the unrelated case-duplicate check reuses the next free number, `TRANS009`, not `TRANS008`) — languages like pt-BR/es-ES share too many legitimate cognates for that heuristic to be reliable.
  4. Scan `*.xaml` files for `{maui:Translate Key}` / `{localize:Translate Key}` references not present in the base (`TRANS001`, fatal).
  5. Scan `*.cs` files (excluding `*.Designer.cs`) for `AppResources.Key` references not present in the base (`TRANS004`, fatal).
  6. Diff the base `.resx` against its `Designer.cs` and append missing generated properties (`TRANS003`).

  `LintService` also exposes progress via the `OnProgress` event (used to stream step-by-step status to the web UI over SignalR) and two static helpers reused by the web UI's translation editor: `LoadTranslationData` (builds a full per-key/per-language matrix) and `SaveTranslation` (writes an edited value back into the correct `.resx` via `XDocument`).

  Diagnostic codes (`TRANS001`–`TRANS007`, plus `TRANS009` for case-only duplicate keys — `TRANS008` was retired, see below) and exit codes are documented in `README.md` — keep both in sync when adding new codes.

- **`Web/WebStartup.cs`** — builds and runs a minimal ASP.NET Core app (Kestrel on `localhost`, auto-picks a free port starting from the requested one) serving the static `wwwroot/index.html` SPA plus a `/api/*` minimal-API surface: run lint (`/lint/run`), discover `.resx` groups under a directory (`/project/resx-files`), discover `.csproj`/`.sln` under a root (`/project/discover`), load/save translation data (`/translate/resx-data`, `/translate/save`), AI-assisted translation (`/translate/ai`, `/translate/providers`), auto-fix preview+apply (`/lint/auto-fix`), and self-update (`/update/check`, `/update/install`). Also maps a SignalR hub at `/hubs/lint`.

- **`Web/LintHub.cs`** — the SignalR hub (`LintHub : Hub`) alternate path for running lint with live progress pushed to the caller (`RunLint` method), used by the web UI as an alternative to the polling-free `/api/lint/run` + `ConnectionId` combo.

- **`Services/TranslationService.cs`** — AI translation provider integration used by the web UI's translate-assist feature.

- **`Services/UpdateService.cs`** — self-update mechanism: checks the NuGet flat-container API for the latest version and, on install, writes a detached `.bat` script to temp that kills the current process (releasing the file lock on the running `.exe`), runs `dotnet tool update --global ResxLint`, and restarts. See the `self-updater-locks-own-exe` learned skill for the reasoning behind this pattern.

- **`Models/ResxModels.cs`** — all cross-layer records in one file (`LintRequest`, `LintResult`, `LintIssue`, `LintSummary`, `AddKeyRequest`/`AddKeyResult`, `RemoveKeyRequest`/`RemoveKeyResult`, translation-editor DTOs, `ProjectResxInfo`, `ProgressMessage`, etc.). Add new DTOs here rather than scattering record definitions across files.

- **`Services/AddKeyService.cs`** — implements `add-key`: refuses on any case-insensitive key collision (never creates a `TRANS009` situation), otherwise writes the base `.resx`, every sibling language `.resx` (explicit `--lang` value or a `[TRANSLATE: ...]` placeholder — never silently skipped), and the `Designer.cs` property, all in one call. Supports `--what-if` and `--update` (update an existing exact-case key's value; does not bypass the case-collision refusal).

- **`Services/RemoveKeyService.cs`** — implements `remove-key`: deletes one *exact* key (case-insensitive only as a lookup convenience — refuses if more than one case-variant matches) from every `.resx` + `Designer.cs`. Optional `--project-dir` runs a live-usage safety check before deleting.

- **`wwwroot/`** — the web UI is a single static `index.html` (no separate frontend build step/bundler) plus a vendored `signalr.min.js`. Edit it directly; there is no npm/vite pipeline to run.

## Key conventions

- All filesystem scans (`EnumerateFiles` in `LintService`) explicitly skip `obj/` and `bin/` directories — replicate this when adding new file-scanning logic.
- `.resx` XML is read via both regex (fast duplicate-detection pass) and `XDocument` (structured passes) — regex is intentionally used only where line-level match positions are needed for MSBuild-style error output (`GetLineNumber`).
- Any change to which XAML/C# syntax patterns are recognized as translation-key usages happens in the regexes at the top of `Step4_XamlReferences` / `Step5_CSharpReferences` in `LintService.cs`.
## Design Context

This project uses `/impeccable` (the Impeccable design skill) for all UI/UX work. Design decisions live in two root files:

- **`PRODUCT.md`** — register (`product`), platform (`web`), two-audience strategy (CLI devs + web UI translators), brand personality, anti-references, design principles
- **`DESIGN.md`** — cyan-teal accent (OKLCH), dark-default theme, Inter + JetBrains Mono, radius/shadow/motion tokens

Always read both files before making UI changes. `/impeccable live` is configured for in-browser iteration on `wwwroot/index.html`.

- The MSBuild integration pattern that downstream consumers use (calling `resx-lint` as a `BeforeTargets="BeforeBuild"` `Exec` task, expecting exit code `1` to trigger a rebuild) is documented in `README.md` — this repo's own build does not exercise that integration, so changes to exit-code semantics should be cross-checked against the README examples.
