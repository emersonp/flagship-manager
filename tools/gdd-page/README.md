# The GDD as a page

Builds one self-contained HTML page from the GDD: the index `docs/gdd.md` and the chapters in `docs/gdd/`. The page is published as a private Claude artifact; the GDD in the repository is the source, and the page is never edited by hand.

```
python tools/gdd-page/build.py /tmp/starship-sim-gdd.html tools/gdd-page/template.html
```

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
- `template.html` holds the styles and the scripts. The output is not committed. Publish it to the same artifact so the link stays the same.

To track progress, edit the Status column in section 11.1 (`docs/gdd/11-roadmap.md`), rebuild, and publish.

## On another machine or another Claude account

Everything the page needs is in this repository; the only requirement is Python 3 (standard library only).

1. Fetch the repository.
2. From its root: `python tools/gdd-page/build.py /tmp/starship-sim-gdd.html tools/gdd-page/template.html`
3. Ask Claude to publish `/tmp/starship-sim-gdd.html` as an artifact. An artifact belongs to the account that published it, so the first publish from a new account makes a new private link there; later publishes of the same file in that session update it, and from a later session Claude is given that link to update.

The page is a single file with no build step beyond this script: fonts come from Google Fonts, and there are no other external resources.

The chosen interface design is published the same way, from `docs/design/bridge-mockup.html` (already a finished page; no build).
