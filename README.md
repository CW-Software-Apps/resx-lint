![resx-lint banner](banner.png)

<p align="center">
  <a href="https://www.nuget.org/packages/ResxLint"><img src="https://img.shields.io/nuget/v/ResxLint?color=1a237e&label=nuget" alt="NuGet"></a>
  <a href="https://www.nuget.org/packages/ResxLint"><img src="https://img.shields.io/nuget/dt/ResxLint?color=00bcd4&label=downloads" alt="Downloads"></a>
  <a href="https://github.com/CW-Software-Apps/resx-lint/actions"><img src="https://github.com/CW-Software-Apps/resx-lint/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"></a>
</p>

---

**resx-lint** is a .NET global tool that validates `.resx` localization keys against your XAML and C# source files. It catches missing keys before they reach production, auto-fixes common problems, and integrates directly into your build pipeline so errors appear inline in Visual Studio, Rider, and any CI output.

## Features

- **Static analysis** — scans `{maui:Translate Key}` in XAML and `AppResources.Key` in C# and reports missing keys as build errors
- **Auto-fixes** — removes duplicate keys, adds missing `Designer.cs` properties, and backfills orphaned language entries automatically
- **MSBuild-native errors** — emits `file(line): error TRANS001: ...` format so VS/Rider show inline squiggles with clickable links
- **Similar key suggestions** — when a key is missing, suggests the closest matches to help spot typos quickly
- **`--what-if` mode** — preview every change without touching any file
- **CI-friendly** — `--fail-on-warnings` escalates warnings to errors for stricter pipelines
- **Zero dependencies** — single self-contained executable, no extra NuGet packages required

---

## Installation

### Global (local dev)

```bash
dotnet tool install --global ResxLint
```

### Per-repo (recommended for teams and CI)

```bash
dotnet new tool-manifest        # creates .config/dotnet-tools.json — commit this file
dotnet tool install ResxLint
```

New devs and CI agents just run once:

```bash
dotnet tool restore
```

---

## Usage

```bash
resx-lint --project-dir <dir> --resx-file <path> [options]
```

| Option | Description |
|---|---|
| `--project-dir <dir>` | Root of the project (where `.xaml` and `.cs` files live) |
| `--resx-file <path>` | Path to the base `.resx` file (e.g. `Resources/AppResources.resx`) |
| `--what-if` | Preview all changes without writing any files |
| `--fail-on-warnings` | Treat TRANS006 and TRANS007 as fatal errors |
| `--quiet` | Suppress ✓ OK and ℹ INFO messages — only show problems |
| `--help` | Show help and exit |

---

## MSBuild Integration

Drop this target into your `.csproj` to validate on every build:

```xml
<Target Name="ValidateTranslations" BeforeTargets="Build">
  <Exec Command="resx-lint --project-dir &quot;$(ProjectDir)&quot; --resx-file &quot;$(ProjectDir)Resources\AppResources.resx&quot;" />
</Target>
```

Strict mode for CI (warnings become errors):

```xml
<Target Name="ValidateTranslations" BeforeTargets="Build">
  <Exec Command="resx-lint --project-dir &quot;$(ProjectDir)&quot; --resx-file &quot;$(ProjectDir)Resources\AppResources.resx&quot; --fail-on-warnings" />
</Target>
```

---

## CI/CD Integration

### GitHub Actions

```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.x'

- name: Restore tools
  run: dotnet tool restore

- name: Validate translations
  run: resx-lint --project-dir . --resx-file Resources/AppResources.resx --fail-on-warnings
```

### Docker / Coolify / Self-hosted

If your CI/CD builds via Docker (e.g. Coolify on Hostinger), install the tool in your `Dockerfile` and run it before `dotnet publish`:

```dockerfile
# Install resx-lint globally
RUN dotnet tool install --global ResxLint
ENV PATH="${PATH}:/root/.dotnet/tools"

# Validate translations before building — fails the Docker build if keys are missing
RUN resx-lint \
    --project-dir /src/app \
    --resx-file /src/YourLibrary/Resources/AppResources.resx \
    --fail-on-warnings
```

Place the `RUN resx-lint ...` step **after** all source files are copied but **before** `dotnet publish`:

```dockerfile
COPY [".", "/src/app"]

# ✅ Validate translations
RUN resx-lint --project-dir /src/app --resx-file /src/YourLibrary/Resources/AppResources.resx

# Build
RUN dotnet publish MyApp.csproj -f net10.0-android -c Release
```

> **Tip:** The tool exits with code `3` on fatal errors, which causes the Docker build to fail and stops the Coolify deployment before a broken build reaches production.

---

## Diagnostic Codes

| Code | Severity | Description | Action |
|---|---|---|---|
| `TRANS001` | ❌ Fatal | Key used in `{maui:Translate Key}` (XAML) not found in base `.resx` | Fix manually |
| `TRANS002` | 🔧 Auto-fix | Duplicate key found in a `.resx` file | Extra occurrences removed |
| `TRANS003` | 🔧 Auto-fix | Key in base `.resx` has no corresponding property in `Designer.cs` | Property added automatically |
| `TRANS004` | ❌ Fatal | Key accessed as `AppResources.Key` (C#) not found in base `.resx` | Fix manually |
| `TRANS005` | 🔧 Auto-fix | Key exists in a language file but not in the base `.resx` | Added to base with `[TRADUZIR]` placeholder |
| `TRANS006` | ⚠️ Warning | Key in base `.resx` has no translation in one or more language files | Add translation or escalate with `--fail-on-warnings` |
| `TRANS007` | ⚠️ Warning | Base `.resx` value is empty or a placeholder like `[TRADUZIR]` | Translation pending |
| `TRANS008` | ℹ️ Info | Value is identical to the base language in a translated file | May be intentional (proper nouns, numbers, etc.) |

Fatal errors (`TRANS001`, `TRANS004`) stop the build immediately. Auto-fixes (`TRANS002`, `TRANS003`, `TRANS005`) modify files and return exit code `1` so MSBuild restarts the build to re-validate.

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | All OK — no issues found |
| `1` | Auto-fixes were applied — restart the build to re-validate |
| `2` | Invalid parameters |
| `3` | Fatal errors found (TRANS001 or TRANS004) |

---

## Output Sample

```
═══ 4/6 Validating XAML usage ({maui:Translate}) ═══

Views/HomePage.xaml(42) : error TRANS001 : Translation key 'WelcomeTitel' not found in AppResources.resx. Similar keys: 'WelcomeTitle'.

──────────────────────────────────────────────────────────────
 SUMMARY — resx-lint
──────────────────────────────────────────────────────────────
  .resx base          : Resources\AppResources.resx
  Keys in base        : 312
  Languages           : 2  (AppResources.en-US.resx, AppResources.es-ES.resx)
  Auto-fixes applied  : 0
  Warnings            : 0
  Fatal errors        : 1
──────────────────────────────────────────────────────────────

  ✖ Build cancelled: 1 fatal error(s). Fix the missing keys and rebuild.
```

---

## Requirements

- .NET 10 SDK or later

---

## License

MIT © [CW Software](https://github.com/CW-Software-Apps)
