# Option E, Bridge: hand-off

Source of truth: `option-e-bridge.html` (same folder). Dark only. Everything below is copied from its `:root`.

## Tokens

```css
:root {
  color-scheme: dark;
  /* spacing */
  --space-3xs: 2px; --space-2xs: 4px; --space-xs: 8px; --space-sm: 12px;
  --space-md: 16px; --space-lg: 24px; --space-xl: 40px; --space-2xl: 64px;
  /* type scale, ratio 1.2 from 16px */
  --text-xs: 0.75rem; --text-sm: 0.875rem; --text-md: 1rem; --text-lg: 1.2rem;
  --text-xl: 1.44rem; --text-2xl: 1.728rem; --text-3xl: 2.074rem; --text-4xl: 2.488rem;
  /* motion */
  --ease-out: cubic-bezier(0.16, 1, 0.3, 1);
  --dur-micro: 120ms; --dur-short: 220ms;
  /* layout */
  --container: 1440px;
  /* colour: blue-slate ground, hue 260, about half Midnight's chroma */
  --color-paper: oklch(15.5% 0.024 260);    /* ground */
  --color-paper-2: oklch(19.5% 0.026 260);  /* wells */
  --color-paper-3: oklch(24% 0.028 260);    /* raised: selected rows, newest beat, current phase, call well */
  --color-ink: oklch(95% 0.01 250);
  --color-ink-2: oklch(85% 0.02 252);
  --color-muted: oklch(73% 0.025 256);
  --color-rule: oklch(30% 0.03 260);        /* hairlines */
  --color-rule-2: oklch(41% 0.035 258);     /* control edges, spine, table head rule */
  --color-accent: oklch(78% 0.12 235);      /* the one azure signal */
  --color-accent-ink: oklch(17% 0.03 260);  /* text on azure */
  --color-accent-soft: oklch(30% 0.05 240); /* selected segment, pressed answer */
  --color-focus: oklch(82% 0.12 235);
  --color-harm: oklch(72% 0.12 25);         /* state only, always beside its word */
  --color-caution: oklch(86% 0.075 88);     /* state only: a waiting call */
  /* faces */
  --font-display: "Source Serif 4", ui-serif, Georgia, serif;
  --font-body: "Host Grotesk", ui-sans-serif, system-ui, sans-serif;
  --font-outlier: "Atkinson Hyperlegible Mono", ui-monospace, "SFMono-Regular", Menlo, monospace;
  /* shape */
  --radius-well: 8px; --radius-inner: 6px; --radius-control: 5px; --radius-chip: 4px;
  --rule-hair: 1px;
  --tracking-display: -0.012em;
  --z-sticky: 200;
}
```

Contrast (WCAG): ink 14.2:1 or better on every surface; ink-2 10.4:1 or better; muted 6.9:1 on paper-3 (the worst case), 5.7:1 on accent-soft; harm 6.3:1 on paper-3; caution 10.8:1 on paper-3; accent-ink on accent 9.8:1.

Where azure is allowed: the primary button, the current phase step, the current route stage, the newest-beat node, the selected segment, native checkbox and radio fills (via `accent-color`), the pressed answer, and focus rings. Nowhere else. Figures are ink, never azure.

## Fonts to self-host (Google Fonts)

| Family | Weights / axes | Role |
| --- | --- | --- |
| Source Serif 4 | variable `opsz` 8 to 60, weight 400 | display |
| Host Grotesk | 400, 500 | body, labels, buttons, tables, placeholder ids |
| Atkinson Hyperlegible Mono | 400 | every measured figure |

Set `font-optical-sizing: auto` on display elements so the optical size follows the font size (sturdier at 20px, crisper at 40px). `font-synthesis: none` on body. Display text uses `font-variant-numeric: lining-nums`; mono uses `tabular-nums slashed-zero`.

## Type roles

| Role | Face | Size | Weight |
| --- | --- | --- | --- |
| Ship name | display | 2xl (27.6px) | 400 |
| View head (`h1`: objective, assignment) | display | 3xl (33px), 2xl at 400px or less | 400 |
| Aftermath verdict | display | 4xl (40px), 3xl at 400px or less | 400 |
| Well and section head (`h2`) | display | xl (23px) | 400 |
| Call head | display | 2xl, xl at 400px or less | 400 |
| Log stage line | display | lg (19.2px) | 400 |
| Body, log text, ids | body | md (16px) | 400 |
| Crew names in tables, objective names | body | md | 500 |
| Labels, notes, table heads, kinds | body | sm (14px); log kinds xs at 400px or less | 400 (table heads 500) |
| Buttons | body | md (segments: sm) | 500 |
| Figures (rolls, targets, deltas, levels, weeks, counts) | mono | sm (14px); readouts 3xl; week and cycle lg | 400 |
| Log line numbers, phase numbers | mono | xs | 400 |

The display face is never used on labels, buttons, table text, or figures. Heads are sentence case. Placeholder ids in capitals stay at 16px or more outside dense tables.

## Components

- **Top bar and phase steps.** One well (`paper-2`, hairline, radius 8, `overflow: hidden`) as a grid of cells split by 1px `rule` lines: ship cell (name plus legacy name, muted), clock cell (`dt` muted sm, `dd` mono lg), then the phases: an `ol` of five equal cells, each with a mono number, name (500), and state word ("Done", "Current", or "Next"). The current cell has `aria-current="step"`, a `paper-3` fill, an inset 2px azure bottom edge, and its number and state word in azure 500. Under 60rem the phases drop to a full-width second row. The row scrolls sideways inside itself at narrow widths, and script scrolls the current cell into view.
- **Well.** `paper-2`, 1px `rule` border, radius 8, no shadow. Head row: `h2` left, note right, padding 16/24/12. Body padding 0 24 24 (16 at 40rem or less). Wells sit 24px apart on the ground; never nest a well in a well.
- **Table.** Full width, horizontal rules only (`rule`, head rule `rule-2`). Base 16px; body cells 14px; crew cells 16px at 500. Numeric cells are right-aligned, mono, and in ink, with 4px side padding. First and last cells pad to 24px (16px narrow). Wide tables scroll in an `overflow-x: auto` wrapper; at 40rem or less the roster's crew column is pinned (`position: sticky; left: 0`, with the row's background).
- **Buttons.** 40px tall with a 44px hit target (`::before` inset -2px 0), radius 5, padding 0 24, 500. Secondary: transparent, 1px `rule-2`; hover `paper-3` with a `muted` edge. Primary (one per view): azure fill, `accent-ink` text; hover `focus`. Active: 1px drop. Disabled: opacity 0.55, `not-allowed`, and the native attribute. Pressed answer: `accent-soft` fill and azure edge. No pills, shadows, or glow.
- **Segmented choice.** Fieldset with legend (muted sm). Row of three equal labels in one 1px `rule-2` box, radius 5, split by hairlines, 36px with a 44px hit target. Radios stay in flow at 1px. Checked: `accent-soft` fill, ink text, inset 2px azure bottom edge. Focus ring inset (-2px).
- **Checkbox and radio rows.** Native 18px inputs with `accent-color: azure`, each in a 44px square `.hit` label. Selected crew and objective rows get a `paper-3` row fill. A disabled row (recovering crew) is muted with disabled inputs and an `aria-label` that gives the reason. Gear rows are 44px lines split by hairlines: box, id (500), kind (muted sm, right). At the loadout limit, the unchecked boxes disable and the count reads "2 of 2".
- **Log trail.** `ol` in a well. Row grid: number (2rem, mono xs, right-aligned) | node (1.5rem) | kind (4.5rem, muted sm) | text, with a 12px gap. One 1px `rule-2` spine runs through the node column from the first row to the well's bottom edge. Row kinds:
  - stage: a hairline above (not on the first), 16px top padding, a 9px square `ink-2` node, and the line set in display lg ("Stage 3: ENCOUNTER_01. Comms blocked.") with the momentum and strain readout in mono sm, right-aligned (wraps below when narrow).
  - check: a 7px hollow round node. The roll and target are mono; the result word is ink 500 ("success", "partial"), or harm for "failure".
  - effects: deltas in mono.
  - notice: the whole line in muted.
  - injury: kind label and result word in harm.
  - call: see the newest beat.
  
  At 40rem or less the kind moves above the text and the columns shrink to 1.25rem.
- **Newest beat.** `paper-3` row with radius 6 and an 8px spread (box-shadow in the same colour, used as padding, not as elevation), an 11px azure node, kind in ink 500, and a "Newest" tag on the right (1px `rule-2`, radius 4, xs). Carries `aria-current="true"`.
- **Route.** In a well above the log: a muted caption, then three hairline boxes (1px `rule-2`, radius 6), joined by short hairline connectors, holding "Stage N" (muted sm, figure in mono), the encounter id (16px), and an optional note. The current stage has an azure edge and connector, and "Stage 3, current" in azure 500. Readouts sit under the route, split by a hairline (beside it when the main column is 60rem or wider). There is no grid or texture behind the route.
- **Meter.** Label (muted sm), figure in mono 3xl plus "of 5" in muted sm, then five 16 by 6px cells with a 1px `rule-2` edge. Filled cells are `ink-2`, never azure. `role="meter"` with min, max, and now values.
- **Call well.** Its own well directly after the log: `paper-3` fill and a 1px caution edge. The log's spine continues through the 24px gap and into the well, ending at an 11px hollow caution ring. Content aligns to the log's kind column. Order: status line "Call waiting for your answer" (caution, sm 500, `role="status"`); head "CREW_01 is calling"; inline facts (Decision, Stakes in mono); "The lead prefers: Safe"; then three equal secondary buttons (Bold, Safe, and Withdraw), with "Lead's preference" (xs muted) under Safe. None of the three is primary.
- **Roster.** A well beside the main column (sticky at top 16px on wide screens). Head, a key line ("A figure in brackets is the level while injured."), then the table. Away-team state, shown in Resolution and Aftermath only, is a `paper-3` row fill plus a tag under the name ("Away team, lead" or "Away team"). Harm: "1 light injury" and "Dead" in harm, next to their words; the effective levels in brackets are in harm too. Recovering is muted. A dead row is all muted, and its name drops to 400.
- **Aftermath lists.** Crew changes is an `ol` in a well that mirrors the log: mono number | kind ("skill", "injury", or "loyalty"; muted sm, injury in harm) | sentence with its figure in mono, split by hairlines. Unattended is a well holding one sentence: figures in mono, "failed" in harm 500.
- **Action bar.** A `paper-2` well at the foot of the view. On the left, the count ("Team 3 of 2 to 3", figures in mono). On the right, the secondary action, then the primary.
- **Mock strip** (mock-up only, drop in the game): a dashed bottom rule, with dashed buttons that turn solid `ink-2` when pressed. Never azure.

## Breakpoints

- Viewport 84rem or wider: the deck is `minmax(43rem, 1fr) minmax(0, 43rem)` and the roster is sticky. Below that, the roster stacks under the main column.
- Viewport 60rem or narrower: the top bar's phase row moves to its own line.
- Viewport 40rem or narrower: 16px inner padding, log and changes rows stack the kind above the text, the route goes vertical, the roster's crew column is pinned, and heads step down one size.
- The main column is a container (`container-type: inline-size`). The route and readouts sit side by side at 60rem or wider; Gear and Operational parameters pair at 42rem or wider.
- The page gutters are `max(var(--space-md), env(safe-area-inset-left/right, 0px))` on the outer wrapper. Nothing scrolls horizontally from 320px up; wide tables scroll inside their wrappers.

## Motion

Motion shows state and is never decoration. Only `background-color`, `border-color`, and `color` transition, at `--dur-micro` with `--ease-out`, plus a 1px `translateY` on press. Nothing animates on load, nothing loops, and focus rings appear instantly. Under `prefers-reduced-motion: reduce`, durations drop to 0.01ms and the press drop is removed.
