# The GDD as a page

Builds one self-contained HTML page from `docs/gdd.md`: the sections, the "try it" calculators for the check and the call rule, and the progress table in section 11.1 with its status chips. The page is published as a private Claude artifact; the GDD in the repository is the source, and the page is never edited by hand.

```
python3 tools/gdd-page/build.py /tmp/starship-sim-gdd.html tools/gdd-page/template.html
```

- `build.py` reads `docs/gdd.md` and fills `template.html`. A table whose last column is headed "Status" gets a chip per row: "Done …", "In progress", "Next", and anything else (shown muted).
- `template.html` holds the styles, the scripts, and the version line under the title; bump that line when publishing.
- The output is not committed. Publish it to the same artifact so the link stays the same.

To track progress, edit the Status column in section 11.1 of `docs/gdd.md`, rebuild, and publish.

## On another machine or another Claude account

Everything the page needs is in this repository; the only requirement is Python 3 (standard library only).

1. Fetch the repository.
2. From its root: `python3 tools/gdd-page/build.py /tmp/starship-sim-gdd.html tools/gdd-page/template.html`
3. Ask Claude to publish `/tmp/starship-sim-gdd.html` as an artifact. An artifact belongs to the account that published it, so the first publish from a new account makes a new private link there; later publishes of the same file in that session update it, and from a later session Claude is given that link to update.

The page is a single file with no build step beyond this script: fonts come from Google Fonts, and there are no other external resources.

The chosen interface design is published the same way, from `docs/design/bridge-mockup.html` (already a finished page; no build).
