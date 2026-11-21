# The Tuning page: mock-up (1f part one, Task 8)

`tuning.html` is the page mocked up from the editor's own stylesheet before any Razor, as `docs/screens/1e-6/` did for the expression widget. Open it from the repo: it links `src/Web/wwwroot/css/app.css` and the fonts beside it, and has no script (its switches work through `:has()`, as a mock-up only). The rules in its `<style>` under "the page" are the ones meant for the page's stylesheet in Task 9. `tuning-1440.png` and `tuning-400.png` are headless Chrome at those widths, full height (the 400 one through a 400px frame, since Chrome's window does not go that narrow).

Design: `docs/plans/2026-11-21-phase-1f-design.md`, section 4. Plan: `docs/plans/2026-11-21-phase-1f-part-one-plan.md`, Task 8.

## The figures are real

Every figure is from `SimRunner bench` on the game's pack, seed 1, 10,000 runs (1,000 in Every objective), with `--out` for the sums of squares. The draft is one number, `compounding.strain.failure` at 1 (saved 2), chosen only so the mock-up reads as the worked example does: rookie loss 34.7% falls to 18.1%, veterans do not move. It is not Task 14's choice. Greying is `BenchNoise.Within` applied to those figures, the same arithmetic, except an encounter's checks (below, answer 4). The draft form is read from `content/tuning/missions.yaml` in file order with its own comments as hints, following Task 3's rules (joined continuation comments, a nested map's comment at its group's head). The replay is the real seed 6691 trace (the worst rookie run under the draft); its prose is written from the shared lines for the mock-up, and the page will show the bridge's own.

The one exception: "Every objective with a real team" (the last state) has illustrative figures, since the command line cannot bench a real team.

## What's where

**Wide (84rem and up, the deck's breakpoint).** Two columns: the draft at the left (28 to 34rem), the bench at the right.

- **The draft** is one form well. Its head: "Draft", the file, a link to its text view; the draft's line ("1 change. Begun at 14:32 today, and held in the server's memory: a restart loses it."); Discard and Save; the alert slot. Then each section in file order: `check`, `compounding`, `develop`, `calls`, `experience`, `injury`, `result`, the read-only block, `bench`.
- **The bench**: the mode tabs (Mission) at the left and the view tabs (One mission, Every objective) at the right of one row; then wells for Inputs (with Run), Figures, Encounters, Replay, and the Mission log.

**Narrow (below 84rem).** Stacked, the draft first, with a "To the bench" link in its head. Every table that has more than one strength's columns (a sweep, Every objective) shows one strength at a time, chosen by a "Strength shown" switch above it. Nothing scrolls sideways from 320px up.

**States below the page** (each under a dashed caption): running, no draft, stale, a draft that does not load, saved, "Bench this" in the aftermath, a real team's inputs, Every objective (generated sweep), and Every objective with a real team.

## The two open items (design section 8)

**How Every objective reads on a narrow screen.** As a sweep does: one strength at a time. On a narrow bench (its container under 44rem) a "Strength shown" switch (Rookie, Mixed, Veteran) sits above the table, and the table shows that strength's Saved, Draft, and Change. Each objective is a row group: its name (the link that opens One mission with the same inputs), its id, and the team size it takes, then four rows (full success %, loss %, deaths per 100 crew-missions, calls per mission). A long objective list grows downward only, never sideways, and every figure stays a table cell rather than a card. Wide, all three strengths sit side by side. With a real team there is one column set, so there is no switch.

Why not a sideways scroll with the label column pinned: nine numeric columns at 400px is two screens wide, and the comparison that matters (saved against draft, one strength) would be split across the scroll. Why not one card per objective: four figures times three strengths times two tunings is too much for a card, and cards lose the column alignment that makes a change easy to scan.

**Whether the read-only sections fold away by default.** Yes. `storylets`, `salience`, `vignettes`, `captainsTime`, and `history` sit together in their place in file order (between `result` and `bench`), under one line: "Measured in Legacy mode (part two); edit as text for now.", with "edit as text" linking to `/dev/content/files/missions`. Each section is folded by default, its summary giving the name and how many numbers it holds ("salience 4 numbers"). Opened, it shows its note, and each value as text in muted with its hint, not as a disabled box (a disabled box at 55% opacity reads as broken, and these are not broken, only measured elsewhere). Folded, the form is the numbers the bench can measure.

## Other decisions

- **The draft column holds still on a wide screen.** It is sticky, scrolls inside itself, and pins its head (Save, Discard, the draft's line), so a number and the figures it moved are in view together. Narrow, it scrolls with the page.
- **A field is a row:** the key as the file writes it (so it matches the text view and the loader's errors), the control at the right, the hint under the key across the row; rows split by hairlines so a key and its value read across the gap. Numbers are plain text boxes, right-aligned in the mono face (`Field`'s rule: the form's words, not the browser's, explain a bad one). A dice field is two steppers, "2 dice of 6 sides"; a range field two, "1 to 3". On a narrow draft a dice or range field puts its key above its steppers.
- **A changed field** carries a 3px ink-2 edge at its left (as the Chronicle's weighty entries, never azure), its key in ink 500, and "saved 2" just before its box.
- **A nested map** (`tiers`, `strain`, `teams.rookie`) is an indented group under a hairline at its left, its key as a small head and its comment beside it. This is where Task 3's "a nested key's comment is its first field's hint" shows, at the head of the group it explains.
- **One primary: Run.** Save and Discard are secondary. The page is one view, and Run is the action repeated; Save is deliberate, and the editor's alert under it says what it did.
- **The mode tabs are drawn with one tab** (Mission), so part two adds Legacy beside it rather than a new layout.
- **Inputs** are one control each, in the command line's terms: objective (select), draw (two-part switch), runs and seed (text, the seed as text), team (Generated, Legacy, Test ship), strength (Rookie, Mixed, Veteran, Sweep; generated only), lead style (generated only), priority and autonomy (the briefing's words: Thorough, Balanced, Fast; Ask often, Balanced, Act alone), develop juniors (a checkbox), call policy (Back the lead, Bold, Safe, Withdraw), and gear by tag with each tag's gear named beside it. Every known set is a segmented switch or a select; nothing is typed but runs and seed.
- **The progress line is at the Figures' head** ("10,000 of 10,000 runs in each column, from seed 1", a status), since that is what is changing; Run shows itself busy while a run goes. There is no Stop: any change stops a run, as the design has it, and the line beside Run says so.
- **The summary table** keeps `BenchReport.Summary`'s labels and order, with quiet row-group heads (Results, Harm, Calls and initiatives, Experience per member, Encounter 1 to 6) to break 30 rows. A sweep's columns are grouped by strength under its name, with more air before each group and no vertical rules. A change always has its sign and a true minus; within the noise it is muted (greyed) and read out as ", within the noise". `BenchNoise.Rule` is said once under each table, and `BenchReport`'s note about means under the summary.
- **The worst runs** are the summary's last row: under each Saved and Draft column, its worst seed as a link that fills the Replay and runs it, or "No run was harmed" where no run in that column was (answer 2).
- **Encounters** are their own well, one folded table per encounter (closed by default; the mock-up opens the Sealed Vault), each summary giving the name, the id, and the rookie checks saved and draft. Rows: checks, approach 1 to 4 %, narrowed %, the five tiers %, deaths, incapacitated; the checks' change is greyed like the rest (answer 4). Percentages show one decimal (the command line prints whole numbers, which turns most small changes into "-0"); checks have thousands separators.
- **Replay**: the seed, which strength's team (for a sweep), Saved or Draft, and Replay; then the team in a line (a generated team named by place, answer 1), the result as the aftermath gives it (the verdict, and each member's fate with harm beside its word), then the Mission log as its own well, as play draws it: newest first, rolls under "Roll" and injury rolls under "Detail".
- **Stale** is a raised box at the draft's head (paper-3, a rule-2 edge), not harm, since nothing has failed yet: it names the changed keys and offers Discard and Open the text. If Save is pressed anyway, the editor's refusal is in harm in the head's alert, and each key's sentence beside its field (Task 7's `Place`).
- **A draft that does not load** shows the loader's error under its field in harm; Run is disabled with the reason under it, and "Run saved only" (secondary) beside it.
- **A real team** is the roster as a table: name, how the launch takes them ("Fit", or the launch's refusal in its own words), and a Team checkbox, disabled for a refused member, whose label carries the reason. The lead is a select of the picked, as the plan says, rather than the briefing's radio column. The count and "Built once, by the launch's own code, and used for every run." sit beside Run.

## The author's answers (2026-11-21)

The author reviewed the mock-up and approved it, answering its five questions:

1. **A generated team is named by its place on the team, not as the game's crew.** `TeamGenerator`'s ids are `CREW_01` to `CREW_03`, which the live pack's names would turn into the starting crew (Okafor, Varro, Ilune). The replay and its log call them Lead, Member 2, and Member 3 instead, with the ids in the team line and the rolls, so nobody takes them for a legacy's crew. A real team keeps its names. For Task 12: the names given to `Prose` for a generated team.
2. **A column with no harmed run says "No run was harmed"** in place of a worst-seed link (the veteran team under the draft in the mock-up, whose worst seed would otherwise fall to seed 1). For Task 12, with the worst seed's link.
3. **A refusal keeps the launch's words** ("Crew member 'CREW_04' is not fit."), as mocked.
4. **An encounter's checks get a noise kind: a mean per run**, so their change is greyed like the rest. Task 10 adds it to `BenchNoise` (a `BenchRow` for the checks, kind Mean) and the per-run sum of squares it needs to `EncounterFigures`, as Task 1 did for the summary's means; `src/` is unchanged here. The mock-up has no such squares, so it judges the checks as a count per run (its variance taken as its mean), which greys the mixed and veteran changes and not the rookie one.
5. **"Bench this" appears in the aftermath** in developer mode, for the mission just finished: its objective, the legacy as the source, and the team that was sent with its lead. Task 13, Step 4 adds it; the mock-up shows it as a dashed developer link beside the verdict's line.
