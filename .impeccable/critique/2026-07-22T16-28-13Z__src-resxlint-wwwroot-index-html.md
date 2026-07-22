---
target: src/ResxLint/wwwroot/index.html
total_score: 22
p0_count: 0
p1_count: 3
timestamp: 2026-07-22T16-28-13Z
slug: src-resxlint-wwwroot-index-html
---
## Critique Report: `src/ResxLint/wwwroot/index.html`

**Method**: dual-agent (A: ses_0755a8c3effec2WKVETdRextxy · B: inline detect.mjs)

---

### Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Step progress excellent; no skeleton states, no save indicator on editor |
| 2 | Match System / Real World | 3 | Dev-appropriate language; "Intelligent" in title, hardcoded flag emoji |
| 3 | User Control and Freedom | 2 | Cancel buttons exist; no undo, no Esc handling, `confirm()` native dialog |
| 4 | Consistency and Standards | 3 | Card/button components consistent; emoji clashing, badge label shown twice |
| 5 | Error Prevention | 2 | Confirm before destructive actions; API key in plain localStorage, no autosave |
| 6 | Recognition Rather Than Recall | 2 | Recent projects with metadata; verbose dropdowns, no visual matrix preview |
| 7 | Flexibility and Efficiency of Use | 1 | Zero keyboard shortcuts; quick-lint is only accelerator |
| 8 | Aesthetic and Minimalist Design | 3 | Clean layout, good OKLCH palette; emoji clash, dense filter bar |
| 9 | Error Recovery | 2 | Toast errors, auto-fix preview; raw API errors forwarded, `alert()` for preview |
| 10 | Help and Documentation | 1 | Empty states teach; no help icon, no tooltips, no diagnostic code guidance |
| **Total** | | **22/40** | **Acceptable** — significant improvements needed |

### Cognitive Load Assessment

- Single focus: ⚠️ Partial — lint view shows too many simultaneous action buttons
- Chunking: ✓ Cards break content well
- Grouping: ✓ Related items grouped
- Visual hierarchy: ✓ Mostly clear
- One thing at a time: ✓ Views are separate
- Minimal choices: ⚠️ At limit — 6+ interactive elements in lint view
- Working memory: ✓ Good — results shown inline
- Progressive disclosure: ✓ AI config hidden in modal

**Verdict**: Low-to-moderate — 1-2 failures

---

### Anti-Patterns Verdict

**LLM assessment**: Mild-to-moderate AI slop signals. The OKLCH palette and 6-step progress pipeline show genuine UX craft. But emoji icons (📁🔍🌐), "Intelligent" in title, `alert()` for fix previews, and a shared modal class for three different purposes are the tells. The interface looks ~70% finished — the bones are right, the skin needs work.

**Deterministic scan**: 2 findings — overused font (Inter — low priority, permitted by product register) and 10 em-dashes (in select placeholders like "— select .resx —"). Both are minor.

---

### Overall Impression

The web UI has a solid architecture: good color system using modern OKLCH, clear 3-view organization, meaningful progress feedback, and well-structured CSS variables. The 22/40 score reflects unfinished details rather than fundamental flaws. The biggest gap is between the tool's brand promise ("Precise, confident, clean") and UI elements that feel casual or unfinished — emoji icons, `alert()` dialogs, plain-text API key storage.

---

### What's Working

**6-step lint progress** — active/done/error dots map to the documented pipeline, giving developers at-a-glance status during long runs.

**OKLCH palette implementation** — faithful to DESIGN.md with blue-tinted neutrals (hue 260), semantic status colors with matching bg variants, shadows using OKLCH black. Modern color science, not a pattern library default.

**Project cards with quick actions** — name, path, metadata badges, and rescan/lint/delete in one compact row. `quickLint` pre-fills the lint view and auto-runs for single-resx projects.

---

### Priority Issues

**[P1] Emoji as icon system** — Every nav button, badge, and empty state uses emoji (📁🔍🌐📦🌓✖⚠ℹ). Unstyleable, OS-inconsistent, conveys Slack-chat informality at odds with a CI-integrated dev tool. Fix: replace with inline SVG or reliable Unicode symbols using `currentColor`.

**[P1] No keyboard shortcuts** — Zero accelerators for a tool whose primary audience is .NET/MAUI developers living in keyboard-driven IDEs. Fix: Ctrl+S save, Ctrl+Enter run lint, Ctrl+1/2/3 switch view, Esc close modals.

**[P1] API key in plain localStorage** — `localStorage.setItem('resx-lint-ai-key', apiKey)` in cleartext, persisted indefinitely. Fix: default to `sessionStorage`, add "remember" checkbox, mask stored value.

**[P2] `alert()` for fix preview** — Native browser dialog for the highest-stakes interaction (files on disk). Fix: styled inline panel with checkboxes and before/after diff per fix.

**[P2] Translation editor lacks undo/optimistic save** — Full reload on every save, no per-cell revert, no dirty indicators. Fix: cell-level revert, optimistic DOM updates, Ctrl+Z support.

---

### Persona Red Flags

**Alex (Power User — .NET developer)**: No keyboard shortcuts. Lint requires 4 interactions. Editor reloads everything on save. No batch edit. API key has no session-only option.

**Jordan (First-Timer — Translation Manager)**: "Lint" is jargon. Resx file selector uses developer terminology. AI config has no cost/scope explanation. Diagnostic codes TRANS001-008 have no tooltips. No onboarding flow.

**Riley (Stress Tester — QA)**: No virtualization at 500+ keys. No max-height on translation textareas. No conflict detection for concurrent saves. No recovery on SignalR disconnect mid-lint. No `dir="auto"` for RTL languages.

**Project-specific: Carla (Translation Lead)**: Manages 10+ language files across multiple projects. Wants batch operations, column-level AI translation per language, export/import spreadsheet. Will find the single-cell workflow tedious and the AI batch (limited to 20 keys at a time) a bottleneck.

---

### Minor Observations

1. Title tag "Intelligent RESX Localization & Translation Studio" — drop "Intelligent"
2. Summary cards show badge label twice (value + label text + badge with same label)
3. `getLangFlag()` maps `en` to 🇺🇸 but `en-GB` also matches — imprecise
4. Version badge uses inline `style.color` mutation instead of class toggling
5. `h2` uses inline `font-size:18px` instead of a CSS variable
6. No `dir="auto"` on translation textareas — RTL languages affected
7. Toast has no close button or early dismiss
8. Filter chips hide checkboxes with `display: none` — breaks screen reader
9. Scan / Run Audit two-step flow is confusing (Scan populates dropdown, Run Audit runs lint)

---

### Questions to Consider

1. **What if the tool had zero modals?** Add Project, AI Config, Update — all could be inline panels or dedicated views.
2. **What if the translation matrix worked like Google Sheets?** Arrow-key navigation, Ctrl+Z, multi-cell select, paste from clipboard.
3. **What if the app loaded directly into the last project's lint results** instead of the empty projects view?
4. **Does the 3-tab structure serve the user or the backend?** A persistent sidebar with project selector + current status would reduce context switching.
5. **What if icons were removed entirely?** Nav labels are descriptive enough alone — "Projects", "Audit & Lint", "Translation Matrix".
