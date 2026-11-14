#!/usr/bin/env bash
# The content editor in a real browser through playwright-cli, on a scratch copy of content/ with developer mode on: rename a crew
# member, a skill, and an encounter through the forms; start a legacy and see the names; play until the encounter's new name is in
# the mission log; see an encounter and a text file refused with nothing written; add a stage to an objective and see it saved.
# Scratch database and content, a free port, and everything removed on exit. Not part of check.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"

port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')
tmp=$(mktemp -d)
base="http://127.0.0.1:$port"
session="edit-$$"
app_group=""
browser=""
# The most cycles played while waiting for the renamed encounter to be drawn.
max_cycles=15

cleanup() {
  if [ -n "$browser" ]; then
    (cd "$tmp" && timeout 30 playwright-cli -s="$session" close > /dev/null 2>&1) || true
  fi
  if [ -n "$app_group" ]; then
    kill -TERM -- "-$app_group" 2>/dev/null || true
    for _ in $(seq 1 50); do
      kill -0 -- "-$app_group" 2>/dev/null || break
      sleep 0.1
    done
    kill -KILL -- "-$app_group" 2>/dev/null || true
  fi
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

# The editor writes the files it reads, so the app reads a copy of content/, never the repository's.
before_repo=$(sum_of "$root/content")
status_before=$(git -C "$root" status --porcelain content)
cp -r "$root/content" "$tmp/content"

# The app is its own session leader, so one signal to its group stops dotnet run and the app it starts.
(cd "$root" && ASPNETCORE_URLS="$base" Web__DatabasePath="$tmp/edit.db" Web__ContentDirectory="$tmp/content" Web__BeatDelayMs=0 Web__DeveloperMode=true ASPNETCORE_ENVIRONMENT=Production \
  setsid bash -c 'echo $$ > "$0"; exec dotnet run --no-build --project src/Web' "$tmp/app.pid" > "$tmp/app.log" 2>&1 &)
for _ in $(seq 1 60); do
  [ -s "$tmp/app.pid" ] && break
  sleep 0.1
done
app_group=$(cat "$tmp/app.pid")

ready=""
for _ in $(seq 1 60); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/" || true)" = "200" ]; then
    ready=1
    break
  fi
  sleep 0.5
done
[ -n "$ready" ] || fail "GET / never answered 200 within 30 seconds."

crew_name="NAME_PLACEHOLDER_CREW"
skill_name="NAME_PLACEHOLDER_SKILL"
encounter_name="NAME_PLACEHOLDER_ENCOUNTER"

browser=1
pw open "$base/dev/content" > /dev/null
wait_for "document.querySelector('#h-crew') !== null" "the editor's index"

# Rename a crew member, a skill, and an encounter through their forms.
save_name "/dev/content/crew/CREW_01" "$crew_name"
grep -q "name: $crew_name" "$tmp/content/crew/starting.yaml" || fail "the crew file does not hold the new name."
save_name "/dev/content/skills/SKILL_A" "$skill_name"
grep -q "name: $skill_name" "$tmp/content/skills/base.yaml" || fail "the skills file does not hold the new name."
save_name "/dev/content/encounters/ENCOUNTER_01" "$encounter_name"
grep -q "name: $encounter_name" "$tmp/content/encounters/base.yaml" || fail "the encounters file does not hold the new name."

# A new legacy shows both names in its briefing without a restart.
visit "/"
pw fill "#name" "LEGACY_ONE" > /dev/null
pw click "getByRole('button', { name: 'Start a legacy' })" > /dev/null
wait_for "location.pathname.startsWith('/legacy/')" "the bridge to open"
pw click "getByRole('button', { name: 'To briefing' })" > /dev/null
wait_for "document.querySelector('#briefing') !== null" "the briefing"
[ "$(js "document.body.textContent.includes('$crew_name')")" = "true" ] || fail "the briefing does not show the crew member's new name."
[ "$(js "document.body.textContent.includes('$skill_name')")" = "true" ] || fail "the briefing does not show the skill's new name."

# Plays one cycle from the briefing: the first objective, as many crew as it needs at least, and the first of them as lead; every call
# is answered the first way. Stops as soon as the renamed encounter is in the log, with the mission still on screen.
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

cycles=1
play_mission
while [ -z "$seen" ]; do
  [ "$cycles" -lt "$max_cycles" ] || fail "the renamed encounter was not drawn in $max_cycles cycles."
  pw click "getByRole('button', { name: 'To aftermath' })" > /dev/null
  wait_for "document.querySelector('.verdict') !== null" "the aftermath"
  pw click "getByRole('button', { name: 'Next cycle' })" > /dev/null
  wait_for "document.querySelector('[aria-current=step] .phase__name').textContent === 'Orders'" "the next cycle's orders"
  pw click "getByRole('button', { name: 'To briefing' })" > /dev/null
  wait_for "document.querySelector('#briefing') !== null" "the briefing"
  cycles=$((cycles + 1))
  play_mission
done

# Refusals write nothing. A field check: an encounter with no careful skill.
files_before=$(sum_of "$tmp/content")
visit "/dev/content/encounters/ENCOUNTER_02"
pw select "#f-careful-skill" "" > /dev/null
pw click "getByRole('button', { name: 'Save' })" > /dev/null
wait_for "document.querySelector('#f-careful-skill-error') !== null" "the field error on the careful skill"
[ "$(js "document.querySelector('#f-careful-skill-error').textContent")" = "Choose a skill." ] || fail "the careful skill's error was not 'Choose a skill.'."
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

console=$(pw --raw console)
case "$console" in
  *"Errors: 0,"*) ;;
  *) fail "the browser console has errors: $console" ;;
esac

[ "$(sum_of "$root/content")" = "$before_repo" ] || fail "the repository's content/ changed."
[ "$(git -C "$root" status --porcelain content)" = "$status_before" ] || fail "git status of the repository's content/ changed."
echo "Browser: content edited and refused. OK."
