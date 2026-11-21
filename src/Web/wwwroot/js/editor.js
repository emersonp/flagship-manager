// The content editor's one script. A chip in the "What plays here" panel, in an approach's tab, or under an expression widget's text
// inserts its text at the caret of the box the author was last in (within that chip's group), or of its first box. A stepper's buttons
// step its box by one (1e part six). Everything else in the editor is Datastar.
let last = null;

const groups = ".expr, .plays__group, .tabs__panel";

document.addEventListener("focusin", (event) => {
  if (event.target instanceof HTMLTextAreaElement && event.target.closest("#plays, .tabs__panel, .expr")) {
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
}
