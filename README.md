![resx-lint banner](https://raw.githubusercontent.com/CW-Software-Apps/resx-lint/master/banner.png)

[![NuGet](https://img.shields.io/nuget/v/ResxLint?color=1a237e&label=nuget)](https://www.nuget.org/packages/ResxLint)
[![Downloads](https://img.shields.io/nuget/dt/ResxLint?color=00bcd4&label=downloads)](https://www.nuget.org/packages/ResxLint)
[![CI](https://img.shields.io/github/actions/workflow/status/CW-Software-Apps/resx-lint/ci.yml?label=CI)](https://github.com/CW-Software-Apps/resx-lint/actions)
[![MIT License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**resx-lint** is a .NET global tool that validates `.resx` localization keys against XAML and C# source files. It catches missing, duplicate, and untranslated keys before they ship — with a full **web UI dashboard** for visual translation editing.

---

## Features

### 🔍 CLI Linter
- **Static analysis** — scans `{maui:Translate Key}` in XAML and `AppResources.Key` in C#
- **Auto-fixes** — removes duplicate keys, adds missing `Designer.cs` properties, backfills orphaned language entries
- **MSBuild-native errors** — emits `file(line): error TRANS001: ...` for inline squiggles in VS/Rider
- **Similar key suggestions** — suggests closest matches for typos
- **`--what-if` mode** — preview every change without touching files
- **CI-friendly** — `--fail-on-warnings` escalates warnings to errors

### 🖥️ Web UI Dashboard
- **Translation Matrix Editor** — edit all language cells side-by-side in a virtual-scrolled grid
- **🇧🇷 Language flags** — locale-to-flag conversion (Regional Indicator Symbols)
- **Configurable base language** — pick your source language (e.g. `pt-BR`) via searchable picker
- **Live missing count** — header badges update in real time as you type
- **AI batch translate** — translate missing/placeholder keys via OpenAI, Anthropic, Azure, Google, DeepSeek, or custom endpoints
- **Auto-fix preview & apply** — see what changes before writing to disk
- **Project manager** — save multiple projects, scan for `.resx` files, quick audit
- **Self-update** — one-click `dotnet tool update` inside the UI
- **Sound effects** — optional audio feedback on save, translate, and errors
- **Dark/Light theme** — persistent toggle
- **🔢 Keyboard shortcuts** — `Ctrl+1/2/3` views, `Ctrl+Enter` audit, `Ctrl+S` save, `Ctrl+F` search, `Esc` close modals
- **Export/Import Excel** — spreadsheet round-trip for translation managers
- **Virtual scrolling** — handles 10,000+ keys without freezing

---

## Screenshots

> TODO: Replace these placeholders with actual screenshots.

| Projects Dashboard | Translation Matrix |
|---|---|
| `[Screenshot: project list with language flags, rescan button, quick audit]` | `[Screenshot: editor grid showing language columns with flags, missing badges, filter bar]` |

| Lint Results | AI Translation Config |
|---|---|
| `[Screenshot: lint summary cards, issue list with auto-fix bar]` | `[Screenshot: AI provider selector, API key, target language]` |

---

## Installation

### Global (local dev)

```bash
dotnet tool install --global ResxLint
```

### Per-repo (recommended for teams and CI)

```bash
dotnet new tool-manifest
dotnet tool install ResxLint
```

New devs and CI agents just run:

```bash
dotnet tool restore
```

---

## Usage

### Interactive Mode

```bash
resx-lint
```

Shows a 3-second prompt:
- Press **W** → opens the **Web UI** at `http://localhost:7950`
- Wait 3s → auto-detects a `.resx` in the current directory and runs the CLI lint

### CLI Lint

```bash
resx-lint --project-dir <dir> --resx-file <path> [options]
```

| Option | Description |
|---|---|
| `--project-dir <dir>` | Root of the project (where `.xaml` and `.cs` files live) |
| `--resx-file <path>` | Path to the base `.resx` file |
| `--what-if` | Preview all changes without writing any files |
| `--fail-on-warnings` | Treat TRANS006 and TRANS007 as fatal errors |
| `--quiet` | Suppress ✅ OK and ℹ INFO messages |
| `--help` | Show help and exit |

### Web UI

```bash
resx-lint --serve
```

Or press **W** in interactive mode. Opens at `http://localhost:7950`.

| Option | Description |
|---|---|
| `--port <port>` | Custom port (default: 7950) |
| `--no-open` | Don't open browser automatically |

### Update Check

```bash
resx-lint --check-update
```

Or click the version badge in the Web UI header.

---

## MSBuild Integration

Add this target to your `.csproj` to run validation on every build:

```xml
<Target Name="ValidateTranslationKeys" BeforeTargets="BeforeBuild">

  <!-- Friendly error if resx-lint is not installed -->
  <Exec Command="resx-lint --help"
        ConsoleToMSBuild="true"
        IgnoreExitCode="true"
        StandardOutputImportance="low"
        StandardErrorImportance="low">
    <Output TaskParameter="ExitCode" PropertyName="_ResxLintExitCode" />
  </Exec>
  <Error
    Condition="'$(_ResxLintExitCode)' == '9009' OR '$(_ResxLintExitCode)' == '127'"
    Text="resx-lint is not installed. Run: dotnet tool install --global ResxLint   |   Docs: https://github.com/CW-Software-Apps/resx-lint" />

  <!-- Run validation -->
  <Exec
    Command="resx-lint --project-dir &quot;$(MSBuildProjectDirectory)&quot; --resx-file &quot;$(MSBuildProjectDirectory)/Resources/AppResources.resx&quot;"
    ConsoleToMSBuild="true"
    IgnoreExitCode="false" />

</Target>
```

> **Linux / CI / Docker:** Always use **forward slashes** in the `--resx-file` path. `$(MSBuildProjectDirectory)/Resources/AppResources.resx` works on both Windows and Linux.

If the tool is not installed, the build stops with a clear message instead of the cryptic `exited with code 9009`.

For strict mode, add `--fail-on-warnings` to the `Exec` command.

---

## CI/CD Integration

### GitHub Actions

```yaml
- name: Validate translations
  run: resx-lint --project-dir . --resx-file Resources/AppResources.resx --fail-on-warnings
```

### Docker / Coolify

```dockerfile
RUN dotnet tool install --global ResxLint
ENV PATH="${PATH}:/root/.dotnet/tools"

COPY [".", "/src/app"]
RUN resx-lint --project-dir /src/app --resx-file /src/YourLibrary/Resources/AppResources.resx

RUN dotnet publish MyApp.csproj -f net10.0-android -c Release
```

Exit code `3` on fatal errors stops the Docker build, preventing broken deployments.

---

## Diagnostic Codes

| Code | Severity | Description | Action |
|---|---|---|---|
| `TRANS001` | ❌ Fatal | Key used in `{maui:Translate Key}` (XAML) not found in base `.resx` | Fix manually |
| `TRANS002` | 🔧 Auto-fix | Duplicate key found in a `.resx` file | Extra occurrences removed |
| `TRANS003` | 🔧 Auto-fix | Key in base `.resx` has no corresponding property in `Designer.cs` | Property added automatically |
| `TRANS004` | ❌ Fatal | Key accessed as `AppResources.Key` (C#) not found in base `.resx` | Fix manually |
| `TRANS005` | 🔧 Auto-fix | Key exists in a language file but not in the base `.resx` | Added to base with `[TRANSLATE]` placeholder |
| `TRANS006` | ⚠️ Warning | Key in base `.resx` has no translation in one or more language files | Add translation or escalate with `--fail-on-warnings` |
| `TRANS007` | ⚠️ Warning | Base `.resx` value is empty or a placeholder like `[TRANSLATE]` | Translation pending |

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
