---
target: src/ResxLint/wwwroot/index.html
total_score: 27
p0_count: 1
p1_count: 4
timestamp: 2026-07-22T19-12-29Z
slug: src-resxlint-wwwroot-index-html
---
# Design Critique: resx-lint Web UI

## Method
Dual-agent (A: ses_074d1e0d1ffez3yxW3awpybEr1 · B: ses_074d1d8c1ffe1m8XRbfeXxNPnG)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | No loading state on initial render; no per-cell saving indicator in editor |
| 2 | Match System / Real World | 3 | "My Projects" slightly informal for dev tool; "Audit & Lint" redundant |
| 3 | User Control and Freedom | 3 | No abort for running lint; no undo after save |
| 4 | Consistency and Standards | 3 | `.ai-config-modal` reused for unrelated modals; mixed inline/styles |
| 5 | Error Prevention | 2 | No confirmation before discard; no save validation; no unsaved-changes warning |
| 6 | Recognition Rather Than Recall | 3 | Icon-only header buttons untitled and unlabeled |
| 7 | Flexibility and Efficiency | 3 | Tab key trapped in textarea cells; no batch edit/multi-select |
| 8 | Aesthetic and Minimalist Design | 3 | Side-stripe toast (slop); gradient logo; verbose `<title>` |
| 9 | Error Recovery | 2 | No undo after save; discard has no safety; technical error messages |
| 10 | Help and Documentation | 2 | No docs link; no explanation of TRANS codes; no tooltips on icons |
| **Total** | | **27/40** | **Acceptable** |

## Anti-Patterns Verdict

### LLM Assessment
**Partial AI slop.** The side-stripe 4px border-left toast (line 1251) is the most concrete violation of the absolute bans. Cumulative minor issues (gradient logo, glassmorphism on shortcuts bar, hover-lift on cards, verbose "Intelligent ... Studio" title) collectively create a borderline "AI-made" feel but the UI largely avoids the major templates. The product register is clear and the vocabulary is consistent — a strong foundation.

### Deterministic Scan
Detector found 2 minor warnings, both false positives:
- **Overused font (Inter)**: deliberately chosen per DESIGN.md
- **Em-dash overuse**: 10 em-dashes are UI affordances (empty cell markers), not prose

The detector caught nothing that the design review missed. All real issues were found by Assessment A.

## Overall Impression
A solid foundation with real craft — the OKLCH palette, consistent tokens, keyboard shortcuts, and progress feedback show intentional design. The UI is functional and largely trustworthy. The biggest gaps are accessibility (zero ARIA, keyboard-trapped filters) and safety nets (no undo, no abort, no discard confirmation). The side-stripe toast is the one genuine slop artifact.

## What's Working
1. **Real-time lint pipeline progress** — 6-dot step bar + SignalR streaming sets the right expectation for a multi-stage operation
2. **Auto-fix preview with trust language** — "nothing has been written to disk yet" embodies the brand perfectly
3. **File-locked update recovery** — numbered, actionable steps turn a frustrating error into guided resolution (best-crafted moment)
4. **Keyboard shortcuts bar** — permanent, discoverable, unobtrusive

## Priority Issues

### P0 — Blocking
- **[P0] Side-stripe toast (line 1251)** — 4px `borderLeft` on `style.borderLeft` violates absolute ban. Replace with full border or background color change.

- **[P0] Filter chips keyboard-inaccessible (lines 624‑628)** — `<input type="checkbox">` with `display: none` inside `<label>` removes it from tab order and accessibility tree. Use `<button role="checkbox" aria-checked>`.

### P1 — Major
- **[P1] No abort for running lint** — Once audit starts there is no cancel button. 120s lockout with no escape.
- **[P1] Zero ARIA attributes** — No `role=""`, `aria-*`, or `tabindex` in 1793 lines. Icon-only buttons have no accessible names. Modals lack `role="dialog"`.
- **[P1] No undo after editor save** — Saving clears dirty state permanently. Brand promises "Confidence through clarity" but there's no rollback.
- **[P1] Discard Changes has no confirmation** — Destructive action with no safety net.

### P2 — Minor
- **[P2] Summary cards: 6 items** — Exceeds 4-item chunking guideline
- **[P2] Step progress: 6 labeled steps** — Same chunking violation
- **[P2] `.ai-config-modal` class reused** — Misleading name for Add Project / Update modals
- **[P2] Icon-only header buttons** — Update check and theme toggle have no `title` or `aria-label`

### P3 — Polish
- **[P3] Inline `style` attributes throughout** — Mix presentational styles in HTML
- **[P3] Verbose `<title>`** — "Intelligent RESX Localization & Translation Studio" ≠ brand voice
- **[P3] No loading state on initial load** — Brief blank screen before render
- **[P3] No documentation link in UI** — TRANS codes and README unreachable from interface
- **[P3] Theme toggle icon always sun** — Never switches to moon icon

## Persona Red Flags

### Alex (Power User)
- Tab key trapped in textarea cells instead of moving grid focus
- No multi-select or batch fill in editor
- AI batch translate does sequential per-language saves (slow)
- **Green flag:** Ctrl+1/2/3, Ctrl+Enter, Ctrl+S, Ctrl+F all work

### Jordan (First-Timer)
- Icon-only buttons for update check and theme toggle — no labels, no titles
- "Audit & Lint" vs "Scan" — Jordan doesn't know which to press first
- "Discard Changes" — one click loses all work, no confirmation
- **Green flag:** Empty state says "Click + Add Project above" — clear next step

### Sam (Accessibility)
- **Zero ARIA** — no roles, no labels, no live regions
- Filter chips completely unreachable by keyboard
- Icon-only buttons have no accessible names
- Modal backdrop click-off not implemented
- **Green flag:** `:focus-visible` defined, `prefers-reduced-motion` respected, `escHtml` DOM-safe

### Carlos (MAUI Developer)
- No MSBuild-style error output for copy-paste
- No configuration persistence between CLI and web UI
- Projects stored in localStorage — not shareable or CI-syncable
- No way to match exact CI lint settings in the UI
- **Green flag:** One-click audit if single .resx — minimal friction

## Minor Observations
- `getLangLabel` does simple `includes('en')` — would match "zen"
- `resolvePath` is a stub — doesn't actually call server
- `escHtml` creates new DOM element per call — GC pressure on large editors
- Theme toggle icon always shows sun regardless of current theme
- Base column in editor uses `opacity: 0.7` — could confuse users expecting prominence

## Questions to Consider
1. "You can start a lint but you can't stop one — what happens on a 50,000-key project?"
2. "Editor saves one language at a time — why not batch all languages into one POST?"
3. "'Translate with AI' targets only missing/placeholder keys — what if I want to retranslate 'identical' keys?"
4. "The logo has a gradient. Cards lift on hover. Shortcuts bar has blur. What information does each carry?"
5. "If the CLI and web UI do the same lint, why do projects live in localStorage instead of a config file that both can share?"
