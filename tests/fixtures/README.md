# Test content

`content/` here is the placeholder pack the tests, the golden hashes, the bench figures, and the scripts (`check.sh`, `tests/browser/play.sh`, `tests/browser/edit.sh`) are pinned to. It is not the game's content (that is the repository's `content/`, which can change freely).

It changes only on purpose, together with the pins that depend on it.

`content/testships/` holds a small test ship for the fixture's own storylets and vignettes (slice 1e, part six). The loader skips the folder, so it moves no golden; the editor and the tests read it with `ContentLoader.LoadTestShips`.
