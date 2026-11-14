# The GDD as a page

Builds one self-contained HTML page from `docs/gdd.md`: the sections, the "try it" calculators for the check and the call rule, and the progress table in section 11.1 with its status chips. The page is published as a private Claude artifact; the GDD in the repository is the source, and the page is never edited by hand.

```
python3 tools/gdd-page/build.py /tmp/starship-sim-gdd.html tools/gdd-page/template.html
```

- `build.py` reads `docs/gdd.md` and fills `template.html`. A table whose last column is headed "Status" gets a chip per row: "Done …", "In progress", "Next", and anything else (shown muted).
- `template.html` holds the styles, the scripts, and the version line under the title; bump that line when publishing.
- The output is not committed. Publish it to the same artifact so the link stays the same.

To track progress, edit the Status column in section 11.1 of `docs/gdd.md`, rebuild, and publish.
