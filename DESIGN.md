# resx-lint Design System

## Theme

dark (default), light toggle available. The tool is a developer dashboard run at localhost — it lives in a terminal or editor context where dark suits the ambient light. Light mode exists for shared-screen scenarios and personal preference.

## Brand Color

```oklch
--brand:        oklch(0.65 0.19 210)
--brand-hover:  oklch(0.72 0.19 210)
--brand-soft:   oklch(0.65 0.19 210 / 0.15)
--brand-ink:    oklch(0.26 0.08 210)
```

Vivid cyan. Technical without being cold; precise without being sterile. Reads like a terminal cursor or an active breakpoint — the tool is working, it's paying attention. On dark backgrounds it carries a subtle glow; on light it anchors with authority.

## Palette

### Dark

```oklch
--bg:        oklch(0.14 0.02 260)
--surface:   oklch(0.18 0.025 260)
--surface-2: oklch(0.22 0.03 260)
--border:    oklch(0.27 0.035 260)
--ink:       oklch(0.91 0.015 260)
--ink-2:     oklch(0.64 0.03 260)
--accent:    oklch(0.65 0.19 210)
--accent-hover: oklch(0.72 0.19 210)
--positive:  oklch(0.60 0.17 145)
--positive-bg: oklch(0.60 0.17 145 / 0.14)
--critical:  oklch(0.58 0.22 25)
--critical-bg: oklch(0.58 0.22 25 / 0.14)
--warning:   oklch(0.72 0.16 75)
--warning-bg: oklch(0.72 0.16 75 / 0.14)
--info:      oklch(0.62 0.12 210)
--info-bg:   oklch(0.62 0.12 210 / 0.14)
--neutral:   oklch(0.55 0.025 260)
--neutral-bg: oklch(0.55 0.025 260 / 0.14)
```

### Light

```oklch
--bg:        oklch(0.95 0.008 260)
--surface:   oklch(0.99 0.005 260)
--surface-2: oklch(0.89 0.01 260)
--border:    oklch(0.82 0.015 260)
--ink:       oklch(0.18 0.02 260)
--ink-2:     oklch(0.48 0.025 260)
--accent:    oklch(0.52 0.19 210)
--accent-hover: oklch(0.46 0.19 210)
--positive:  oklch(0.50 0.17 145)
--positive-bg: oklch(0.50 0.17 145 / 0.12)
--critical:  oklch(0.50 0.22 25)
--critical-bg: oklch(0.50 0.22 25 / 0.12)
--warning:   oklch(0.62 0.18 75)
--warning-bg: oklch(0.62 0.18 75 / 0.12)
--info:      oklch(0.50 0.14 210)
--info-bg:   oklch(0.50 0.14 210 / 0.12)
--neutral:   oklch(0.55 0.025 260)
--neutral-bg: oklch(0.55 0.025 260 / 0.12)
```

The palette uses a blue-tinted neutral (hue 260) rather than pure gray or warm-toned gray. This keeps the surfaces consistent with the technical register without drifting into the warm-neutral AI default.

## Typography

| Role | Family | Weight | Size (base) |
|---|---|---|---|
| UI | Inter, system-ui, sans-serif | 400 / 500 / 600 / 700 | 14px (body) |
| Code | JetBrains Mono, monospace | 400 / 500 | 13px (inline), 12px (labels) |

Inter for all UI text — clean, economical, widely used in developer tooling. JetBrains Mono for code display: file paths, diagnostic codes, key names.

Body line length 65-75ch. Headings use `text-wrap: balance`.

## Radius

| Token | Value | Use |
|---|---|---|
| `--radius-sm` | 6px | buttons, inputs, chips |
| `--radius-md` | 10px | issue items, filter bar |
| `--radius-lg` | 14px | cards, panels, modals |

## Shadows

```css
--shadow-sm: 0 1px 3px oklch(0 0 0 / 0.08);
--shadow-md: 0 4px 12px oklch(0 0 0 / 0.1);
--shadow-lg: 0 12px 32px oklch(0 0 0 / 0.18);
```

## Motion

Moderate — purposeful micro-interactions that convey state without calling attention to themselves.

- Hover transitions: 150ms ease-out
- Panel/entry transitions: 250ms ease-out
- Progress indicators: spin 600ms linear infinite
- Reduced motion: `@media (prefers-reduced-motion: reduce)` — all transitions instant, all animations paused

## Z-Index Scale

| Layer | Value |
|---|---|
| sticky header | 10 |
| dropdown/popover | 30 |
| modal backdrop | 50 |
| modal panel | 60 |
| toast | 100 |

## Code Conventions

- CSS custom properties for all color, radius, and shadow tokens
- `data-theme="dark|light"` on `<html>` to toggle palette
- No preprocessor; native CSS throughout
- OKLCH for all color values — no hex or rgb in new code
- BEM-inspired naming only when scoping a specific component; avoid when global tokens suffice
