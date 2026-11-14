# Phase 1, slice 1c-editor: the content editor

**Spec:** `docs/gdd.md` sections 3 and 7.4; `docs/plans/phase-1-outline.md`, "Carried forward to the content editor". **Builds on:** `main` at 2026-11-14 (slice 1c).
**Ends with:** the author opens the game in developer mode, renames the crew, skills, gear, objectives, and encounters, changes an encounter's numbers, saves, and sees the change in the running game without a restart. An edit that would break the pack is refused with the reason, and nothing is written.

## 1. Decisions

From the author:

1. **Content is edited in the browser, not in YAML.** The editor writes the same files in `content/`; git stays the record (2026-11-14).
2. **Crew will be generated in the long run** from authored sources: species, cultures, life paths, and name generators. The crew form stays small; forms for sources are the ones worth investing in (2026-11-14).

Made here, each cheap to reverse:

3. **Everything authored gets a display name.** Skills, crew, gear, objective templates, encounters, decisions, and assignments gain an optional `name`. The id stays the stable key (events, saved legacies, and references use it); every screen and log line shows the name, falling back to the id. Golden hashes do not move.
4. **Developer mode is a setting** (`Web:DeveloperMode`, off by default, on in `run.sh`). With it off the editor's routes do not exist.
5. **A save is all or nothing.** The editor builds the whole pack as it would be after the edit, in memory, and runs the loader on it. Any error: nothing is written, and the errors are shown. No error: the changed file is written atomically, and the running game swaps to the new pack.
6. **The live pack is swappable.** One holder owns the current pack and the game built from it; each request takes one snapshot. A mission in flight keeps the tuning it started with (already true); screens already tolerate an id the pack no longer has.
7. **Ids are fixed once created.** Renaming is what `name` is for. Deleting is allowed when the loader accepts the result (nothing still refers to it).
8. **The editor rewrites a file in one canonical layout.** Comments inside a file are not kept, except a leading comment block, which is. The layout is stable, so git diffs stay small.
9. **References are chosen, not typed.** A skill, a gear tag, an encounter tag, or a chain target is picked from what exists, so most broken references cannot be entered. Field rules the form can check itself (required, ranges) are shown beside the field; the loader's errors for the candidate pack are shown in the form's alert, in the loader's words.
10. **Tuning files are edited as text** in this slice, validated the same way. The page that edits numbers and re-runs the bench side by side is slice 1f.
11. **The editor uses the game's design system** (`docs/design/`): the same tokens, wells, tables, and controls. No new look.
12. **Local only.** The editor writes files on the machine that runs the server, so it exists only in developer mode and is not for hosting. File paths come from the pack's own index, never from a form.

## 2. What can be edited

| Kind | File today | Fields |
|---|---|---|
| Skills | `skills/base.yaml` | id, name |
| Gear | `gear/base.yaml` | id, name, and tag |
| Starting crew | `crew/starting.yaml` | id, name, seniority, a level per skill, caution, and discipline |
| Assignments | `assignments/base.yaml` | id, name, objective tags, offer weights, and loadout limit |
| Objective templates | `objectives/base.yaml` | id, name, tags, team size, need, unattended difficulty, and the stages (slot, draw tags, count, and skippable) |
| Encounters | `encounters/base.yaml` | id, name, tags, chain target, the careful and fast approaches (skill, difficulty, and gear tag), and the decision (id, name, stakes, and the bold and safe effects) |
| Tuning | `tuning/missions.yaml`, `tuning/crew.yaml` | The file's text |
| Pack | `pack.yaml` | Version (the id is fixed) |

A new item goes into the kind's first file. An item is saved back to the file it came from.

## 3. Screens

- **Content** (`/dev/content`): the kinds, each with its items (name and id) and a count; the pack's id and version; whether the pack on disk is valid, with the loader's errors if not; how many legacies exist (a reminder that edits apply to games in progress).
- **A kind's list**: a table of its items with the fields that matter at a glance; "New".
- **An item's form**: its fields; "Save"; "Delete" with a confirmation in the page; the alert for refusals. Lists inside a form (an objective's stages; a decision's effects) have "Add" and "Remove" on each row; these change the form, not the file.
- **A tuning file**: a text area, "Save", and the alert.
- The top bar of the game gains a "Content" link in developer mode; the editor's pages have a link back to the legacies.

## 4. Tests

- **Names:** the loader accepts, trims, and limits a name; the read model and every screen show the name and fall back to the id; events and golden hashes are unchanged.
- **Writing:** every file in the repository's `content/`, rewritten by the editor's writer and loaded again, gives the same pack; a leading comment survives; the layout is stable (writing twice gives the same bytes).
- **Save pipeline:** a valid edit writes one file and swaps the pack; an invalid edit writes nothing and reports the loader's errors; a write that fails part-way leaves the old file; two saves at once apply one after the other.
- **Swap:** a request that started before a swap finishes on the pack it started with; a legacy in mid-mission continues after an edit; a new legacy uses the new starting crew.
- **Requests** (the real app, a scratch copy of `content/`): developer mode off gives 404 for every editor route; each kind's create, edit, and delete; each refusal; ids cannot be changed; paths cannot be steered from a form.
- **In a browser** (scripted, outside the gate): rename a crew member and a skill, see both in a running legacy's roster after a reload; break an encounter (remove its skill) and see the refusal; fix it and save.
- The game's own tests and the gate keep reading the repository's `content/`; the editor's tests never write to it.

## 5. Not in this slice

Species, cultures, life paths, and name generators (their forms arrive with crew generation); text for reports and storylets (slices 1d and 1e); the tuning page with the bench (1f); undo beyond git; editing while hosted.
