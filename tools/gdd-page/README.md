# The GDD as pages

Builds two self-contained HTML pages from the GDD (the index `docs/gdd.md` and the chapters in `docs/gdd/`), each published as a private Claude artifact. The GDD in the repository is the source, and the pages are never edited by hand.

```
python tools/gdd-page/build.py /tmp/ready-room-progress.html tools/gdd-page/template.html --progress
python tools/gdd-page/build.py /tmp/ready-room-gdd.html tools/gdd-page/template.html
```

- **Ready Room Progress** (`--progress`): the overview's "Where the build stands", chapter 11 (the roadmap and the progress table in 11.1), and chapter 16 (the open questions). It is small, so republishing it is cheap. **Republish it in every session that changes 11.1, 16, or the status line.**
- **Flagship: Ready Room GDD**: every chapter. **Republish it only when chapters change in substance** (a slice built, a section rewritten), not for a status line or a new row in 15. Its own "Where the build stands" is as of its last build, and links to the progress page.
- **Why two:** before replacing a published page, Claude must read the live copy in full, once a session. The whole GDD is about 100,000 tokens to read; the progress page about a quarter of that.
- `pages.json` holds both pages' links. On the progress page a reference to a section it does not carry ("section 4.3") links to that section on the GDD's page, in a new tab.

(`python3` where that is the name of Python 3.)

## What the page is

- **One chapter at a time.** A sidebar lists the parts and chapters in the index's order; the open chapter shows its sections beneath it. Each chapter ends with its source file and links to the previous and next chapter. On a phone the sidebar folds behind a "Chapters" button.
- **Links that last.** `#s4` opens chapter 4 and `#s4-2` opens it at section 4.2, so a link to any section can be shared. "section 4.3", "see 5.6", "(3.8)", and "risk 2" in the text link to the section they name, but only when it exists.
- **Search** over every section of every chapter, from the box at the top of the sidebar. Enter opens the first result; Escape clears.
- **The overview** (`#home`, the default): the version and when the page was built, in US Pacific time; where the build stands, read from the progress table in section 11.1 (Phase 1's slices done, the slice in progress or next, and the last one done); and each part with its chapters and their descriptions from the index.
- **Calculators** for the check (after 4.2) and the call rule (after 4.4), and the open questions in chapter 16 as a form whose answers stay in the browser and copy out as text.

## How it reads the source

- `build.py` reads the index for the status line (the version is the `vN.N` in it), the paragraph after it, the parts (each `##` heading other than "How this document is organised", its first paragraph, and its table of chapters), and each chapter's description. Chapters appear in the order the index lists them.
- In a chapter file, `#` is the chapter's title (the page draws its own), `##` starts a section, and `###` is a heading inside one. The "A chapter of the Starship Sim GDD" line under the title is left out.
- A table whose last column is headed "Status" gets a chip per row: "Done …", "In progress", "Next", and anything else (shown muted).
- The build time is stamped in US Pacific time by the US daylight-saving rule, so no time zone data is needed.
- `template.html` holds the styles and the scripts, for both pages. The output is not committed. Publish each to its link in `pages.json` so the links stay the same.

To track progress, edit the Status column in section 11.1 (`docs/gdd/11-roadmap.md`), rebuild the progress page, and publish it.

## On another machine or another Claude account

Everything the page needs is in this repository; the only requirement is Python 3 (standard library only).

1. Fetch the repository.
2. From its root, build both pages as above.
3. Ask Claude to publish each as an artifact. An artifact belongs to the account that published it, so the first publish from a new account makes new private links there: put them in `pages.json` and build again, so the two pages link to each other. Later publishes of the same file in that session update them, and from a later session Claude updates the links in `pages.json`.

The page is a single file with no build step beyond this script: fonts come from Google Fonts, and there are no other external resources.

The chosen interface design is published the same way, from `docs/design/bridge-mockup.html` (already a finished page; no build).
