#!/usr/bin/env bash
# The content editor in a real browser through playwright-cli, on a scratch copy of tests/fixtures/content/ with developer mode on: rename a crew
# member, a skill, and every encounter a first stage can draw through the forms; start a legacy and see the names; play the first
# mission and see an encounter's new name in its log; see an encounter and a text file refused with nothing written; tick boxes in
# rows, remove and move rows, and see the saved values match the page; add a stage to an objective and see it saved; add an encounter approach, move
# between its tabs by keyboard, save, and see the file, the form, and the fit at 400 and 1280 pixels agree.
# Scratch database and content, a free port, and everything removed on exit. Not part of check.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"

# The app is started and stopped by tests/web-app.sh, which needs no setsid and finds Python 3 by either name.
. "$root/tests/web-app.sh"
python=$(web_python)
tmp=$(mktemp -d)
base=""
session="edit-$$"
browser=""

cleanup() {
  if [ -n "$browser" ]; then
    (cd "$tmp" && timeout 30 playwright-cli -s="$session" close > /dev/null 2>&1) || true
  fi
  web_stop
  rm -rf "$tmp"
}
trap cleanup EXIT

fail() {
  echo "Browser: $1" >&2
  echo "--- app log (last 40 lines) ---" >&2
  tail -n 40 "$tmp/app.log" >&2 || true
  exit 1
}

# playwright-cli writes its snapshots under the working directory, so it runs inside the scratch directory.
pw() {
  (cd "$tmp" && timeout 60 playwright-cli -s="$session" "$@")
}

# A JavaScript expression's value, without the quotes the raw output puts around a string.
js() {
  pw --raw eval "$1" | tr -d '"'
}

# Waits up to 30 seconds for a JavaScript expression to be true.
wait_for() {
  for _ in $(seq 1 120); do
    if [ "$(js "$1" 2> /dev/null || true)" = "true" ]; then
      return 0
    fi
    sleep 0.25
  done
  fail "timed out waiting for $2."
}

phase() {
  js "document.querySelector('[aria-current=step] .phase__name').textContent"
}

# A checksum of every file under a folder, to prove what was and was not written.
sum_of() {
  (cd "$1" && find . -type f | LC_ALL=C sort | xargs sha256sum | sha256sum)
}

# Opens a page and waits for its main region.
visit() {
  pw goto "$base$1" > /dev/null
  wait_for "location.pathname === '$1' && document.querySelector('#main') !== null" "$1"
}

# Types a name into a form's name box and saves; waits for the notice.
save_name() {
  visit "$1"
  pw fill "#f-name" "$2" > /dev/null
  pw click "getByRole('button', { name: 'Save' })" > /dev/null
  wait_for "document.querySelector('#form-alert .alert-ok')?.textContent === 'Saved.'" "the save of $1"
}

# Waits for a refusal in the alert and checks it holds the words.
expect_refusal() {
  wait_for "document.querySelector('#form-alert .error') !== null" "a refusal ($1)"
  pw --raw eval "document.querySelector('#form-alert').textContent" | grep -qF -- "$2" || fail "the refusal ($1) did not say '$2'."
}

dotnet build --nologo -v quiet "$root/src/Web" > "$tmp/build.log" 2>&1 || { cat "$tmp/build.log" >&2; exit 1; }

# The editor writes the files it reads, so the app reads a copy of the fixture, never the repository's folders.
fixture="$root/tests/fixtures/content"
before_repo=$(sum_of "$root/content")
before_fixture=$(sum_of "$fixture")
status_before=$(git -C "$root" status --porcelain content tests/fixtures/content)
cp -r "$fixture" "$tmp/content"

web_start "$tmp/app.log" Web__DatabasePath="$tmp/edit.db" Web__ContentDirectory="$tmp/content" Web__BeatDelayMs=0 Web__DeveloperMode=true \
  || fail "the app did not start."
base="$web_base"

crew_name="NAME_PLACEHOLDER_CREW"
skill_name="NAME_PLACEHOLDER_SKILL"
encounter_name="NAME_PLACEHOLDER_ENCOUNTER"

browser=1
pw open "$base/dev/content" > /dev/null
wait_for "document.querySelector('#h-crew') !== null" "the editor's index"

# Rename a crew member, a skill, and every encounter a first stage can draw (each objective's first stage draws by field), so the
# first mission shows one of the new names whichever objective it plays.
save_name "/dev/content/crew/CREW_01" "$crew_name"
grep -q "name: $crew_name" "$tmp/content/crew/starting.yaml" || fail "the crew file does not hold the new name."
save_name "/dev/content/skills/SKILL_A" "$skill_name"
grep -q "name: $skill_name" "$tmp/content/skills/base.yaml" || fail "the skills file does not hold the new name."
for n in 01 02 03 04 05 06 07 08; do
  save_name "/dev/content/encounters/ENCOUNTER_$n" "${encounter_name}_$n"
  grep -q "name: ${encounter_name}_$n" "$tmp/content/encounters/base.yaml" || fail "the encounters file does not hold ENCOUNTER_$n's new name."
done

# A new legacy shows both names in its briefing without a restart.
visit "/"
pw fill "#name" "LEGACY_ONE" > /dev/null
pw click "getByRole('button', { name: 'Start a legacy' })" > /dev/null
wait_for "location.pathname.startsWith('/legacy/')" "the bridge to open"
pw click "getByRole('button', { name: 'To briefing' })" > /dev/null
wait_for "document.querySelector('#briefing') !== null" "the briefing"
[ "$(js "document.body.textContent.includes('$crew_name')")" = "true" ] || fail "the briefing does not show the crew member's new name."
[ "$(js "document.body.textContent.includes('$skill_name')")" = "true" ] || fail "the briefing does not show the skill's new name."

# Plays one mission from the briefing: the first objective, as many crew as it needs at least, and the first of them as lead; every
# call is answered the first way. Stops as soon as a renamed encounter is in the log, with the mission still on screen.
seen=""
play_mission() {
  pw check "input[name=objective] >> nth=0" > /dev/null
  needed=$(js "+document.querySelector('input[name=objective]:checked').dataset.min")
  fit=$(js "document.querySelectorAll('input[name=crew]:not([disabled])').length")
  [ "$fit" -ge "$needed" ] || fail "only $fit crew are fit for a team of $needed."
  for i in $(seq 0 $((needed - 1))); do
    pw check "input[name=crew]:not([disabled]) >> nth=$i" > /dev/null
  done
  pw check "input[name=lead]:not([disabled]) >> nth=0" > /dev/null
  pw click "getByRole('button', { name: 'Launch' })" > /dev/null
  settled="document.querySelector('#log li') !== null && (document.querySelector('#call form') !== null || document.querySelector('#resolution-footer button') !== null)"
  wait_for "$settled" "the launch's beats to play"
  answers=0
  while [ "$(js "document.querySelector('#call form') !== null")" = "true" ]; do
    [ "$answers" -lt 40 ] || fail "the mission did not end within 40 answers."
    if [ "$(js "document.querySelector('#log').textContent.includes('$encounter_name')")" = "true" ]; then
      seen=1
      return 0
    fi
    call=$(js "document.querySelector('#call input[name=call]').value")
    log=$(js "document.querySelectorAll('#log li').length")
    pw click "#call form.answer >> nth=0 >> button" > /dev/null
    wait_for "document.querySelectorAll('#log li').length > $log && (document.querySelector('#resolution-footer button') !== null || (document.querySelector('#call input[name=call]')?.value ?? '$call') !== '$call')" "the answer's beats to play"
    answers=$((answers + 1))
  done
  wait_for "document.querySelector('#resolution-footer button') !== null" "To aftermath"
  if [ "$(js "document.querySelector('#log').textContent.includes('$encounter_name')")" = "true" ]; then
    seen=1
  fi
}

play_mission
[ -n "$seen" ] || fail "no renamed encounter is in the first mission's log."

# Refusals write nothing. A field check: an encounter whose first approach has no skill.
files_before=$(sum_of "$tmp/content")
visit "/dev/content/encounters/ENCOUNTER_02"
pw select "#f-approaches-0-skill" "" > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#f-approaches-0-skill-error') !== null" "the field error on the first approach's skill"
[ "$(js "document.querySelector('#f-approaches-0-skill-error').textContent")" = "Choose a skill." ] || fail "the first approach's skill error was not 'Choose a skill.'."
[ "$(sum_of "$tmp/content")" = "$files_before" ] || fail "a refused encounter save wrote to the content files."

# The loader's refusal: delete the encounter the others chain to.
visit "/dev/content/encounters/ENCOUNTER_CHAIN"
pw click "getByRole('button', { name: 'Delete' })" > /dev/null
pw click "getByRole('button', { name: 'Yes, delete' })" > /dev/null
expect_refusal "deleting a chain target" "Chains to unknown encounter 'ENCOUNTER_CHAIN'"
[ "$(sum_of "$tmp/content")" = "$files_before" ] || fail "a refused delete wrote to the content files."

# A text file the loader refuses keeps what was typed; so does a change to the pack's id.
visit "/dev/content/files/missions"
pw fill "#text" "check: [" > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
expect_refusal "a broken tuning file" "Nothing was written."
[ "$(js "document.querySelector('#text').value")" = "check: [" ] || fail "the refused tuning text was not kept in the text area."
[ "$(sum_of "$tmp/content")" = "$files_before" ] || fail "a refused tuning save wrote to the content files."
visit "/dev/content/files/pack"
pw fill "#text" "$(printf 'id: OTHER\nversion: 1\n')" > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
expect_refusal "a changed pack id" "The pack's id cannot be changed here."
[ "$(sum_of "$tmp/content")" = "$files_before" ] || fail "a refused pack save wrote to the content files."

# A ticked box belongs to its row: remove the row it is in, or the row above it, and the page and the saved file follow the rows.
effects="[...document.querySelectorAll('#f-bold li.block')].map(li => li.querySelector('[name\$=strain]').value + ',' + li.querySelector('[name\$=momentum]').value + ',' + li.querySelector('[name\$=withdraw]').checked).join('|')"
visit "/dev/content/encounters/ENCOUNTER_06"
pw check "#f-bold-0-withdraw" > /dev/null
pw click "#row-remove-bold-0" > /dev/null
wait_for "document.querySelectorAll('#f-bold li.block').length === 1" "the first bold effect to go"
[ "$(js "$effects")" = '1,,false' ] || fail "after removing a ticked effect the one left shows $(js "$effects"), not strain 1 and no withdraw."
pw click "#row-add-bold" > /dev/null
wait_for "document.querySelector('#f-bold-1-momentum') !== null" "the new bold effect"
pw fill "#f-bold-1-momentum" "2" > /dev/null
pw check "#f-bold-1-withdraw" > /dev/null
pw click "#row-remove-bold-0" > /dev/null
wait_for "document.querySelectorAll('#f-bold li.block').length === 1" "the effect above the ticked one to go"
[ "$(js "$effects")" = ',2,true' ] || fail "after removing the effect above a ticked one the page shows $(js "$effects"), not momentum 2 with withdraw."
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#form-alert .alert-ok')?.textContent === 'Saved.'" "the encounter's save"
grep -A14 "id: ENCOUNTER_06" "$tmp/content/encounters/base.yaml" | grep -qF "bold: [{ momentum: 2, withdraw: true }]" || fail "ENCOUNTER_06 was not saved with the one effect the page showed."
[ "$(js "$effects")" = ',2,true' ] || fail "after the save the page shows $(js "$effects")."

# The same for a stage's draw tag and Skippable: tick both on the second stage and move it up. The draw tag redraws the count at
# once; Skippable is ticked last, so only the move's answer can carry it.
stages="[...document.querySelectorAll('#main li.block')].map(li => li.querySelector('[name\$=slot]').value + ':' + [...li.querySelectorAll('[name\$=draw]')].filter(b => b.checked).map(b => b.value).join('+') + ':' + li.querySelector('[name\$=skippable]').checked).join('|')"
visit "/dev/content/objectives/OBJECTIVE_TEMPLATE_2"
pw check "input[name='stages[1].draw'][value=field]" > /dev/null
wait_for "document.querySelector('#stage-1-match')?.textContent === '3 encounters match'" "the second stage's count after ticking field"
[ "$(js "document.activeElement.name + '=' + document.activeElement.value")" = "stages[1].draw=field" ] || fail "focus left the draw tag box after the count was redrawn."
pw check "#f-stages-1-skippable" > /dev/null
pw click "#row-up-1" > /dev/null
wait_for "document.querySelector('#f-stages-0-slot')?.value === 'objective'" "the second stage to move up"
[ "$(js "$stages")" = 'objective:field+technical:true|approach:field+hazard:false|extraction:social:true' ] || fail "after the move the stages show $(js "$stages")."
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#form-alert .alert-ok')?.textContent === 'Saved.'" "the objective's save after the move"
grep -qF "{ slot: objective, draw: { tags: [technical, field] }, count: [1, 2], skippable: true }" "$tmp/content/objectives/base.yaml" || fail "the moved stage was not saved with its ticks."
grep -qF "{ slot: approach, draw: { tags: [hazard, field] }, count: 1 }" "$tmp/content/objectives/base.yaml" || fail "the stage that moved down was saved with ticks it did not have."

# Add a stage to an objective, save, and see it after a fresh load.
visit "/dev/content/objectives/OBJECTIVE_TEMPLATE_2"
[ "$(js "document.querySelectorAll('#main li.block').length")" = "3" ] || fail "OBJECTIVE_TEMPLATE_2 did not show its three stages."
pw click "#row-add" > /dev/null
wait_for "document.querySelector('#stage-3') !== null" "the new stage"
pw fill "#f-stages-3-slot" "SLOT_PLACEHOLDER" > /dev/null
pw check "input[name='stages[3].draw'][value=field]" > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#form-alert .alert-ok')?.textContent === 'Saved.'" "the objective's save"
grep -q "slot: SLOT_PLACEHOLDER" "$tmp/content/objectives/base.yaml" || fail "the objectives file does not hold the new stage."
visit "/dev/content/objectives/OBJECTIVE_TEMPLATE_2"
[ "$(js "document.querySelector('#f-stages-3-slot').value")" = "SLOT_PLACEHOLDER" ] || fail "the new stage is not there after a fresh load."

# The encounter form's approach tabs: add a third approach, name it, write its attempt and one outcome, move between tabs with
# the keyboard, save, reload, and see the file and the form agree. Then neither 400 nor 1280 pixels scrolls sideways.
shown="[...document.querySelectorAll('[role=tabpanel]')].filter(p => !p.hidden).map(p => p.id).join(',')"
visit "/dev/content/encounters/ENCOUNTER_01"
pw click "#row-add-approaches" > /dev/null
wait_for "document.querySelector('#f-approaches-2-id') !== null" "the third approach"
[ "$(js "$shown")" = "panel-approach-2" ] || fail "after adding an approach its tab is not the one shown ($(js "$shown"))."
pw fill "#f-approaches-2-id" "third" > /dev/null
pw fill "#f-approaches-2-name" "APPROACH_C_NAME" > /dev/null
pw select "#f-approaches-2-skill" "SKILL_B" > /dev/null
pw fill "#f-approaches-2-text-attempt" "actor: Third try." > /dev/null
pw check "#f-approaches-2-out-success-own" > /dev/null
wait_for "getComputedStyle(document.querySelector('#f-approaches-2-out-success-strain')).display !== 'none' && document.querySelector('#f-approaches-2-out-success-strain').offsetParent !== null" "the success outcome's fields"
pw fill "#f-approaches-2-out-success-strain" "2" > /dev/null
pw click "#tab-approach-2" > /dev/null
pw press ArrowLeft > /dev/null
[ "$(js "$shown")" = "panel-approach-1" ] || fail "Left did not show the second tab ($(js "$shown"))."
[ "$(js "document.activeElement.id")" = "tab-approach-1" ] || fail "Left did not move focus to the second tab."
pw press Home > /dev/null
[ "$(js "$shown")" = "panel-approach-0" ] || fail "Home did not show the first tab ($(js "$shown"))."
pw press End > /dev/null
[ "$(js "$shown")" = "panel-approach-2" ] || fail "End did not show the last tab ($(js "$shown"))."
pw press ArrowRight > /dev/null
[ "$(js "$shown")" = "panel-approach-0" ] || fail "Right from the last tab did not wrap to the first ($(js "$shown"))."
pw press ArrowLeft > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#form-alert .alert-ok')?.textContent === 'Saved.'" "the encounter's save with a third approach"
[ "$(js "$shown")" = "panel-approach-2" ] || fail "the save did not keep the tab that was shown ($(js "$shown"))."
grep -A12 "id: ENCOUNTER_01" "$tmp/content/encounters/base.yaml" | grep -qF "id: third" || fail "the encounters file does not hold the third approach."
grep -A20 "id: ENCOUNTER_01" "$tmp/content/encounters/base.yaml" | grep -qF "success: [{ strain: 2 }]" || fail "the third approach's success outcome was not saved."
grep -A20 "id: ENCOUNTER_01" "$tmp/content/encounters/base.yaml" | grep -qF 'attempt: "actor: Third try."' || fail "the third approach's attempt was not saved."
visit "/dev/content/encounters/ENCOUNTER_01"
form="['#f-approaches-2-id','#f-approaches-2-name','#f-approaches-2-skill','#f-approaches-2-text-attempt','#f-approaches-2-out-success-strain'].map(s => document.querySelector(s).value).join('|')"
[ "$(js "$form")" = "third|APPROACH_C_NAME|SKILL_B|actor: Third try.|2" ] || fail "after a reload the form shows $(js "$form")."
[ "$(js "document.querySelector('#tab-approach-2').textContent.trim()")" = "APPROACH_C_NAME" ] || fail "the third tab is not labelled with its name."
for width in 400 1280; do
  pw resize "$width" 900 > /dev/null
  [ "$(js "document.documentElement.scrollWidth <= document.documentElement.clientWidth")" = "true" ] || fail "the encounter form scrolls sideways at $width pixels."
  [ "$(js "[...document.querySelectorAll('#item-form *')].every(e => e.getBoundingClientRect().right <= document.documentElement.clientWidth + 0.5)")" = "true" ] || fail "something in the encounter form is wider than the page at $width pixels."
done

console=$(pw --raw console)
case "$console" in
  *"Errors: 0,"*) ;;
  *) fail "the browser console has errors: $console" ;;
esac

[ "$(sum_of "$root/content")" = "$before_repo" ] || fail "the repository's content/ changed."
[ "$(sum_of "$fixture")" = "$before_fixture" ] || fail "tests/fixtures/content/ changed."
[ "$(git -C "$root" status --porcelain content tests/fixtures/content)" = "$status_before" ] || fail "git status of content/ or tests/fixtures/content/ changed."
echo "Browser: content edited and refused. OK."
