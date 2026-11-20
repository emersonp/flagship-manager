#!/usr/bin/env bash
# One cycle in a real browser through playwright-cli: every action button is double-clicked, and the page must move one
# step each time, and the itinerary holds a storylet that must be answered before the orders. Scratch database and copy of tests/fixtures/content, free port, and everything removed on exit. Not part of check.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"

# The app is started and stopped by tests/web-app.sh, which needs no setsid and finds Python 3 by either name.
. "$root/tests/web-app.sh"
python=$(web_python)
tmp=$(mktemp -d)
base=""
session="play-$$"
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

# playwright-cli writes its snapshots under the working directory, so it runs inside the scratch directory. A step that fails
# (no element matches, a timeout) stops the script with what playwright-cli said, rather than set -e's silent exit.
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

dotnet build --nologo -v quiet "$root/src/Web" > "$tmp/build.log" 2>&1 || { cat "$tmp/build.log" >&2; exit 1; }

# The app reads a scratch copy of the tests' placeholder pack, never the game's content/.
cp -r "$root/tests/fixtures/content" "$tmp/content"
# In the copy STORYLET_01 holds while trait_a is 5 or less, so it waits on the first itinerary; either answer sets trait_a to 9,
# so it does not come back.
"$python" - "$tmp/content/storylets/base.yaml" <<'PY'
import sys
path = sys.argv[1]
text = open(path, encoding="utf-8", newline="").read()
edits = [
    ('requires: "ship.trait.trait_a == 1"', 'requires: "ship.trait.trait_a <= 5"'),
    ("{ trait: trait_a, set: 0 }", "{ trait: trait_a, set: 9 }"),
    ("{ trait: trait_a, set: 2 }", "{ trait: trait_a, set: 9 }"),
]
for old, new in edits:
    assert text.count(old) == 1, old
    text = text.replace(old, new)
open(path, "w", encoding="utf-8", newline="").write(text)
PY

web_start "$tmp/app.log" Web__DatabasePath="$tmp/play.db" Web__ContentDirectory="$tmp/content" Web__BeatDelayMs=50 Web__DeveloperMode=false \
  || fail "the app did not start."
base="$web_base"

browser=1
pw open "$base/" > /dev/null
pw fill "#name" "LEGACY_ONE" > /dev/null
pw click "getByRole('button', { name: 'Start a legacy' })" > /dev/null
wait_for "location.pathname.startsWith('/legacy/')" "the bridge to open"
[ "$(phase)" = "Itinerary" ] || fail "a new legacy opened in $(phase), not the itinerary."

# Itinerary: the storylet waits, and To orders is disabled with the reason beside it.
wait_for "document.querySelector('#storylets form.answer') !== null" "a waiting storylet"
[ "$(js "document.querySelector('.actions .act button').disabled")" = "true" ] || fail "To orders is not disabled while a storylet waits."
[ "$(js "document.querySelector('#advance-blocked').textContent")" = "A storylet is waiting for an answer." ] || fail "To orders does not say why it is disabled."

# At 400 and 1280 pixels wide nothing scrolls sideways, and no storylet panel scrolls inside itself.
fits="document.documentElement.scrollWidth <= document.documentElement.clientWidth && [...document.querySelectorAll('#storylets, .storylet')].every(p => p.scrollWidth <= p.clientWidth && p.scrollHeight <= p.clientHeight)"
for width in 400 1280; do
  pw resize "$width" 900 > /dev/null
  [ "$(js "$fits")" = "true" ] || fail "the waiting storylet scrolls at $width pixels wide."
done

# The first answer, double-clicked: the panel settles with its tags, and To orders is free.
pw dblclick "#storylets form.answer >> nth=0 >> button" > /dev/null
wait_for "document.querySelector('#storylets form') === null && document.querySelector('#storylets .fig-tag') !== null" "the storylet to settle"
[ "$(js "document.querySelector('.actions .act button').disabled")" = "false" ] || fail "To orders is still disabled after the answer."

# A reload still shows the settled panel, and it fits at both widths.
js "location.reload()" > /dev/null || true
wait_for "document.readyState === 'complete' && document.querySelector('#storylets .fig-tag') !== null" "the settled storylet after a reload"
for width in 400 1280; do
  pw resize "$width" 900 > /dev/null
  [ "$(js "$fits")" = "true" ] || fail "the settled storylet scrolls at $width pixels wide."
done

# Itinerary: a double click reaches the orders and stops there.
pw dblclick "getByRole('button', { name: 'To orders' })" > /dev/null
wait_for "document.querySelector('[aria-current=step] .phase__name').textContent === 'Orders'" "the orders"
sleep 1
[ "$(phase)" = "Orders" ] || fail "a double click on To orders left the page in $(phase), not Orders."

# Orders: a double click reaches the briefing and stops there.
pw dblclick "getByRole('button', { name: 'To briefing' })" > /dev/null
wait_for "document.querySelector('#briefing') !== null" "the briefing"
sleep 1
[ "$(phase)" = "Briefing" ] || fail "a double click on To briefing left the page in $(phase), not Briefing."

# Briefing: the first objective, as many crew as it needs at least, and the first of them as lead.
pw check "input[name=objective] >> nth=0" > /dev/null
needed=$(js "+document.querySelector('input[name=objective]:checked').dataset.min")
for i in $(seq 0 $((needed - 1))); do
  pw check "input[name=crew]:not([disabled]) >> nth=$i" > /dev/null
done
pw check "input[name=lead]:not([disabled]) >> nth=0" > /dev/null

# The away team card holds every column at 400 pixels wide: neither the card nor the page scrolls sideways.
pw resize 400 900 > /dev/null
card='document.querySelector("#h-crew").closest("section")'
[ "$(js "$card.scrollWidth === $card.clientWidth")" = "true" ] || fail "the away team card scrolls sideways at 400 pixels wide."
[ "$(js "document.documentElement.scrollWidth <= document.documentElement.clientWidth")" = "true" ] || fail "the page scrolls sideways at 400 pixels wide."
pw resize 1280 900 > /dev/null

pw click "getByRole('button', { name: 'Launch' })" > /dev/null
settled="document.querySelector('#log li') !== null && (document.querySelector('#call form') !== null || document.querySelector('#resolution-footer button') !== null)"
wait_for "$settled" "the launch's beats to play"
[ "$(phase)" = "Resolution" ] || fail "the launch left the page in $(phase), not Resolution."

# Resolution: answer every call until the mission ends; the first answer is a double click.
answers=0
while [ "$(js "document.querySelector('#call form') !== null")" = "true" ]; do
  [ "$answers" -lt 40 ] || fail "the mission did not end within 40 answers."
  call=$(js "document.querySelector('#call input[name=call]').value")
  log=$(js "document.querySelectorAll('#log li').length")
  if [ "$answers" -eq 0 ]; then
    pw dblclick "#call form.answer >> nth=0 >> button" > /dev/null
  else
    pw click "#call form.answer >> nth=0 >> button" > /dev/null
  fi
  wait_for "document.querySelectorAll('#log li').length > $log && (document.querySelector('#resolution-footer button') !== null || (document.querySelector('#call input[name=call]')?.value ?? '$call') !== '$call')" "the answer's beats to play"
  answers=$((answers + 1))
done
wait_for "document.querySelector('#resolution-footer button') !== null" "To aftermath"

# Resolution ended: a double click reaches the aftermath and stops there.
pw dblclick "getByRole('button', { name: 'To aftermath' })" > /dev/null
wait_for "document.querySelector('.verdict') !== null" "the aftermath"
sleep 1
[ "$(phase)" = "Aftermath" ] || fail "a double click on To aftermath left the page in $(phase), not Aftermath."

# Aftermath: a double click starts one new cycle, not two.
pw dblclick "getByRole('button', { name: 'Next cycle' })" > /dev/null
wait_for "document.querySelector('[aria-current=step] .phase__name').textContent === 'Itinerary'" "the next cycle's itinerary"
sleep 1
# One turn moves the fixture's calendar 5 to 7 days from 13 MONTH_B 105; two would be 10 or more. Play shows no cycle.
clock=$(js "[...document.querySelectorAll('.clock dd > [aria-hidden=true]')].map(d => d.textContent).join(' ')")
case "$clock" in
  "18 MONTH_B 105" | "19 MONTH_B 105" | "20 MONTH_B 105") ;;
  *) fail "after a double click on Next cycle the date reads '$clock', expected 18, 19, or 20 MONTH_B 105." ;;
esac
[ "$(js "document.querySelectorAll('.clock dt').length")" = "1" ] || fail "the top bar shows more than the date in play."
[ "$(phase)" = "Itinerary" ] || fail "a double click on Next cycle left the page in $(phase), not the itinerary."

# The Chronicle, from the top bar: the founding, the mission, and its calls under their dates, nothing scrolling sideways at 400 or
# 1280 pixels wide; Bridge goes back to the itinerary it left.
pw click "getByRole('link', { name: 'Chronicle' })" > /dev/null
wait_for "location.pathname.endsWith('/chronicle') && document.querySelector('.entries li') !== null" "the Chronicle"
[ "$(js "document.querySelectorAll('.entries li').length >= 2")" = "true" ] || fail "the Chronicle lists fewer than the founding and the mission."
[ "$(js "document.querySelector('.chronicle-day:last-of-type .entries li:last-child .entry__text').textContent.startsWith('CHRONICLE_FOUNDING')")" = "true" ] || fail "the founding is not the Chronicle's oldest entry."
for width in 400 1280; do
  pw resize "$width" 900 > /dev/null
  [ "$(js "document.documentElement.scrollWidth <= document.documentElement.clientWidth")" = "true" ] || fail "the Chronicle scrolls sideways at $width pixels wide."
done
pw click "getByRole('link', { name: 'Notable' })" > /dev/null
wait_for "location.search === '?show=notable'" "the Chronicle's notable entries"
pw click "getByRole('link', { name: 'Bridge' })" > /dev/null
wait_for "document.querySelector('[aria-current=step] .phase__name') !== null && !location.pathname.endsWith('/chronicle')" "the bridge"
[ "$(phase)" = "Itinerary" ] || fail "leaving the Chronicle opened $(phase), not the itinerary."

console=$(pw --raw console)
case "$console" in
  *"Errors: 0,"*) ;;
  *) fail "the browser console has errors: $console" ;;
esac

echo "Browser: one cycle played, a storylet answered, double clicks held, the Chronicle read. OK."
