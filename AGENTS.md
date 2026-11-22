# Notes for agents working in this repository

## `docs/touchstones.md` is the north star

It is the list of games and media to take mechanical and thematic inspiration from for this project.

## `docs/conventions.md` is how things are named

Ids, reserved characters, flags, YAML keys, display names, and files. Follow it in content, code, tests, and documents, and quote it in loader messages and editor hints rather than restating a rule.

## The GDD is one file per chapter

`docs/gdd.md` is the index: the status line and the table of chapters by part. Each chapter is `docs/gdd/NN-slug.md`. Section numbers are permanent ids that code, tests, and plans cite ("GDD 4.2"): never renumber a section, give a new chapter the next unused number, and list it in the index under its part with a one-line description. After a change, rebuild and republish the pages it touches: Ready Room Progress whenever 11.1, 16, or the status line change, and the whole GDD only when chapters change in substance (`tools/gdd-page/README.md`).

## `docs/lore/` is Parker's

It holds the game's lore, in whatever markdown Parker keeps there. Never create, edit, rename, or delete anything in it. Read it freely to understand the setting and its structure, and take in-fiction names, the calendar, and other lore from it into `content/` or the design when a slice needs them, citing the file. Whenever you see uncommitted changes in it (new, modified, deleted, or untracked), commit them as they are, in a commit of their own with a plain message such as "Update the lore". Do not fold them into another commit, and do not wait to be asked.

## `docs/personal_todo.md` is Parker's

It is Parker's own to-do list. Never edit it. Whenever you see uncommitted changes to it (new, modified, or untracked), commit them as they are, in a commit of their own with a plain message such as "Update the personal to-do list". Do not fold them into another commit, and do not wait to be asked.

Be aware of the todo list. If an opportune chance to implement a todo item comes up, offer to Parker to do it.
