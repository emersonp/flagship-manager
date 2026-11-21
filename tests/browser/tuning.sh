#!/usr/bin/env bash
# The Tuning page in a real browser through playwright-cli (slice 1f part one's worked example, on a scratch copy of
# tests/fixtures/content/ so it does not depend on the game's numbers): start a legacy, open Tuning from "Bench this" on an
# objective in Dev Play's Orders, bench a generated rookie team, change compounding.strain.failure in the draft, run, and see
# saved, draft, and change; replay the draft's worst run as the report; save, and see the file change at that number alone;
# then launch the legacy's next mission from the briefing and see it start on the new number. The page must not scroll
# sideways at 400 or 1440 pixels. Scratch database and content, a free port, and everything removed on exit. Not part of check.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"

# The app is started and stopped by tests/web-app.sh, which needs no setsid and finds Python 3 by either name.
. "$root/tests/web-app.sh"
python=$(web_python)
tmp=$(mktemp -d)
base=""
session="tuning-$$"
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
  local out
  if ! out=$(cd "$tmp" && timeout 60 playwright-cli -s="$session" "$@"); then
    fail "playwright-cli $* failed: $out"
  fi
  printf '%s\n' "$out"
}

# A JavaScript expression's value, without the quotes the raw output puts around a string.
js() {
  pw --raw eval "$1" | tr -d '"'
}

# Waits up to 60 seconds for a JavaScript expression to be true.
wait_for() {
  for _ in $(seq 1 240); do
    if [ "$(js "$1" 2> /dev/null || true)" = "true" ]; then
      return 0
    fi
    sleep 0.25
  done
  fail "timed out waiting for $2 (at $(js "location.href" 2> /dev/null || true))."
}

phase() {
  js "document.querySelector('[aria-current=step] .phase__name').textContent"
}

sum_of() {
  (cd "$1" && find . -type f | LC_ALL=C sort | xargs sha256sum | sha256sum)
}

no_sideways_scroll() {
  for width in 400 1440; do
    pw resize "$width" 900 > /dev/null
    [ "$(js "document.documentElement.scrollWidth <= document.documentElement.clientWidth")" = "true" ] || fail "$1 scrolls sideways at $width pixels."
  done
}

dotnet build --nologo -v quiet "$root/src/Web" > "$tmp/build.log" 2>&1 || { cat "$tmp/build.log" >&2; exit 1; }

# The page saves the file it benches, so the app reads a copy of the fixture, never the repository's folders.
fixture="$root/tests/fixtures/content"
before_repo=$(sum_of "$root/content")
before_fixture=$(sum_of "$fixture")
cp -r "$fixture" "$tmp/content"
tuning="$tmp/content/tuning/missions.yaml"
cp "$tuning" "$tmp/missions.before"
grep -q "^    failure: 2$" "$tuning" || fail "the fixture's compounding.strain.failure is not 2."

web_start "$tmp/app.log" Web__DatabasePath="$tmp/tuning.db" Web__ContentDirectory="$tmp/content" Web__BeatDelayMs=0 Web__DeveloperMode=true \
  || fail "the app did not start."
base="$web_base"

browser=1
pw open "$base/" > /dev/null
pw resize 1440 900 > /dev/null
pw fill "getByRole('textbox', { name: 'Name' })" "LEGACY_TUNING" > /dev/null
pw click "getByRole('button', { name: 'Start a legacy' })" > /dev/null
wait_for "location.pathname.startsWith('/legacy/')" "the bridge to open"
id=$(js "location.pathname.split('/')[2]")

# Dev Play, at the orders: "Bench this" on the first objective opens the Tuning page in a new tab, with this legacy as the source.
pw goto "$base/dev/legacy/$id" > /dev/null
wait_for "document.querySelector('#main') !== null" "Dev Play"
pw click "button:text-is('To orders')" > /dev/null
wait_for "document.querySelector('[aria-current=step] .phase__name')?.textContent === 'Orders'" "the orders"
pw click "a.dev-edit >> text=Bench this >> nth=0" > /dev/null
pw tab-select 1 > /dev/null
wait_for "location.pathname === '/dev/tuning' && new URL(location.href).searchParams.get('source') === '$id'" "the Tuning page from Bench this"
objective=$(js "new URL(location.href).searchParams.get('objective')")
[ -n "$objective" ] || fail "Bench this named no objective."
no_sideways_scroll "the Tuning page"
pw resize 1440 900 > /dev/null

# A generated rookie team, 1,000 runs.
pw click "label[for=bi-team-generated]" > /dev/null
wait_for "new URL(location.href).searchParams.get('team') === null && document.querySelector('label[for=bi-strength-rookie]') !== null" "the generated team's inputs"
pw click "label[for=bi-strength-rookie]" > /dev/null
pw fill "#bi-runs" "1000" > /dev/null
pw press Tab > /dev/null
wait_for "new URL(location.href).searchParams.get('strength') === 'rookie' && new URL(location.href).searchParams.get('runs') === '1000'" "the inputs in the address"

# One number in the draft: a failure adds one strain, not two. The file is untouched until Save.
pw fill "[name='compounding.strain.failure']" "1" > /dev/null
pw press Tab > /dev/null
wait_for "document.querySelector('#draft-head')?.textContent.includes('1 change.') && document.querySelector('[name=\"compounding.strain.failure\"]').closest('.tf')?.textContent.includes('saved 2')" "the draft's change"
cmp -s "$tuning" "$tmp/missions.before" || fail "a change in the draft wrote the file."

# Run: saved, draft, and change, on the same seeds.
pw click "button:text-is('Run')" > /dev/null
wait_for "/^1,000 of 1,000 runs in each column/.test(document.querySelector('#bench-figures p')?.textContent ?? '')" "the run to finish"
[ "$(js "[...document.querySelectorAll('#figs-summary thead th.n')].map(t => t.textContent.trim()).join(',') === 'Saved,Draft,Change'")" = "true" ] \
  || fail "the figures are not saved, draft, and change."
loss=$(js "[...document.querySelectorAll('#figs-summary tr')].find(r => r.cells[0]?.textContent.trim() === 'Loss %').innerText.split('\t').slice(1).join(' ')")
echo "Rookie loss % (saved, draft, change): $loss"

# Replay the draft's worst run as the report.
pw click "#figs-summary tr:last-child a >> nth=1" > /dev/null
wait_for "document.querySelector('#bench-replay')?.textContent.includes('under the draft') && document.querySelector('#bench-replay').textContent.includes('Mission log')" "the replay under the draft"
no_sideways_scroll "the replay"
pw resize 1440 900 > /dev/null

# Save: the file changes at that number alone, and the draft ends.
pw click "button:text-is('Save')" > /dev/null
wait_for "document.querySelector('#draft-head')?.textContent.includes('compounding.strain.failure is now 1')" "the save"
diff "$tmp/missions.before" "$tuning" > "$tmp/missions.diff" && fail "Save did not write the file."
[ "$(grep -c '^[<>]' "$tmp/missions.diff")" = "2" ] && grep -q "^<     failure: 2$" "$tmp/missions.diff" && grep -q "^>     failure: 1$" "$tmp/missions.diff" \
  || fail "Save changed more than compounding.strain.failure: $(cat "$tmp/missions.diff")"

# The legacy's next mission, launched from the briefing, starts on the new number.
pw tab-select 0 > /dev/null
pw reload > /dev/null
pw click "button:text-is('To briefing')" > /dev/null
wait_for "document.querySelector('[aria-current=step] .phase__name')?.textContent === 'Briefing'" "the briefing"
template_objective=$(js "[...document.querySelectorAll('#briefing input[name=objective]')][0].value")
pw check "#briefing input[name=objective][value=$template_objective]" > /dev/null
for crew in $(js "[...document.querySelectorAll('#briefing input[name=crew]:not(:disabled)')].slice(0, 2).map(i => i.value).join(' ')"); do
  pw check "#briefing input[name=crew][value=$crew]" > /dev/null
  lead=${lead:-$crew}
done
pw check "#briefing input[name=lead][value=$lead]" > /dev/null
pw click "button:text-is('Launch')" > /dev/null
wait_for "['Resolution', 'Aftermath'].includes(document.querySelector('[aria-current=step] .phase__name')?.textContent)" "the launch"
pw tab-close 1 > /dev/null 2>&1 || true
browser=""
(cd "$tmp" && timeout 30 playwright-cli -s="$session" close > /dev/null 2>&1) || true
web_stop
"$python" - "$tmp/tuning.db" <<'PY' || fail "the next mission did not start on compounding.strain.failure 1."
import re, sqlite3, sys
found = []
db = sqlite3.connect(sys.argv[1])
for row in db.execute("select * from module_state"):
    for value in row:
        text = value.decode("utf-8", "ignore") if isinstance(value, bytes) else str(value)
        found += re.findall(r'"StrainFailure":\s*(\d+)', text)
assert found == ["1"], found
PY

[ "$(sum_of "$root/content")" = "$before_repo" ] || fail "the repository's content/ changed."
[ "$(sum_of "$fixture")" = "$before_fixture" ] || fail "tests/fixtures/content/ changed."
echo "Browser: tuned from Bench this, run, replayed, saved, and the next mission uses it. OK."
