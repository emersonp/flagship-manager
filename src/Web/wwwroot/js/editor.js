// The content editor's one script: a chip in the "What plays here" panel or in an approach's tab inserts its text at the caret
// of the box the author was last in (within that chip's group or tab), or of its first box. Everything else in the editor is Datastar.
let last = null;

document.addEventListener("focusin", (event) => {
  if (event.target instanceof HTMLTextAreaElement && event.target.closest("#plays, .tabs__panel")) {
    last = event.target;
  }
});

document.addEventListener("click", (event) => {
  const chip = event.target instanceof Element ? event.target.closest("[data-insert]") : null;
  const group = chip?.closest(".plays__group, .tabs__panel");
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
  // The panel listens for input to refresh its previews.
  box.dispatchEvent(new Event("input", { bubbles: true }));
});
