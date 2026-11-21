// The content editor's one script. A chip in the "What plays here" panel, in an approach's tab, or under an expression widget's text
// inserts its text at the caret of the box the author was last in (within that chip's group), or of its first box. A stepper's buttons
// step its box by one (an input and a change), and a storylet option's label fills its id box as it is typed, until the id is typed in (1e part six). Everything
// else in the editor is Datastar.
let last = null;

const groups = ".expr, .chipbox, .plays__group, .tabs__panel";

document.addEventListener("focusin", (event) => {
  if (event.target instanceof HTMLTextAreaElement && event.target.closest("#plays, .tabs__panel, .expr, .chipbox")) {
    last = event.target;
  }
});

document.addEventListener("click", (event) => {
  const target = event.target instanceof Element ? event.target : null;
  const step = target?.closest(".stepper [data-step]");
  if (step) {
    stepBy(step);
    return;
  }

  const chip = target?.closest("[data-insert]");
  const group = chip?.closest(groups);
  if (!chip || !group) {
    return;
  }

  const box = last && last.isConnected && group.contains(last) ? last : group.querySelector("textarea");
  if (!box) {
    return;
  }

  const start = box.selectionStart ?? box.value.length;
  const end = box.selectionEnd ?? start;
  box.setRangeText(chip.dataset.insert, start, end, "end");
  box.focus();
  // The panel and the expression widget listen for input to refresh their previews and checks.
  box.dispatchEvent(new Event("input", { bubbles: true }));
});

// A whole number moves by the button's step; a blank or bad one starts from 0, and never goes below the box's least.
function stepBy(button) {
  const box = button.closest(".stepper")?.querySelector("input");
  if (!box) {
    return;
  }

  const now = Number.parseInt(box.value, 10);
  let next = (Number.isNaN(now) ? 0 : now) + Number.parseInt(button.dataset.step, 10);
  const min = Number.parseInt(box.dataset.min ?? "", 10);
  if (!Number.isNaN(min) && next < min) {
    next = min;
  }

  box.value = String(next);
  box.dispatchEvent(new Event("input", { bubbles: true }));
  // A step is a finished change, as a native number box's arrows are: the Tuning page's draft form posts a field on change (1f part one).
  box.dispatchEvent(new Event("change", { bubbles: true }));
}

// The label names its id box with data-id-to; the box follows while its data-auto is "true", and typing in it stops that.
document.addEventListener("input", (event) => {
  const box = event.target;
  if (!(box instanceof HTMLInputElement)) {
    return;
  }

  if (box.dataset.idTo) {
    const id = document.getElementById(box.dataset.idTo);
    if (id instanceof HTMLInputElement && id.dataset.auto === "true") {
      id.value = idFrom(box.value);
    }
  } else if (box.dataset.auto !== undefined) {
    box.dataset.auto = "false";
  }
});

// A lower-case id from a label, as NewIds.FromName makes one: words of letters and digits joined by underscores, the articles dropped,
// marks and apostrophes taken off, no digit or underscore first, at most 40 in all; empty when that leaves nothing.
function idFrom(name) {
  const words = name.normalize("NFD").toLowerCase().replace(/[\u0300-\u036f'\u2019]/g, "").split(/[^a-z0-9]+/).filter((w) => w.length > 0);
  const kept = words.filter((w) => !["a", "an", "the"].includes(w));
  let id = (kept.length > 0 ? kept : words).join("_").replace(/^[0-9_]+/, "");
  if (id.length > 40) {
    id = id.slice(0, 40).replace(/_+$/, "");
  }

  return /^[a-z][a-z0-9_]{0,39}$/.test(id) ? id : "";
}
