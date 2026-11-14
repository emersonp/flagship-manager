#!/usr/bin/env bash
# One cycle in a real browser through playwright-cli: every action button is double-clicked, and the page must move one
# step each time. Scratch database, free port, and everything removed on exit. Not part of check.sh.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"

port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')
tmp=$(mktemp -d)
base="http://127.0.0.1:$port"
session="play-$$"
app_group=""
browser=""

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

dotnet build --nologo -v quiet "$root/src/Web" > "$tmp/build.log" 2>&1 || { cat "$tmp/build.log" >&2; exit 1; }

# The app is its own session leader, so one signal to its group stops dotnet run and the app it starts.
(cd "$root" && ASPNETCORE_URLS="$base" Web__DatabasePath="$tmp/play.db" Web__BeatDelayMs=50 ASPNETCORE_ENVIRONMENT=Production \
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

browser=1
pw open "$base/" > /dev/null
pw fill "#name" "LEGACY_ONE" > /dev/null
pw click "getByRole('button', { name: 'Start a legacy' })" > /dev/null
wait_for "location.pathname.startsWith('/legacy/')" "the bridge to open"

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
wait_for "document.querySelector('[aria-current=step] .phase__name').textContent === 'Orders'" "the next cycle's orders"
sleep 1
clock=$(js "[...document.querySelectorAll('.clock dd')].map(d => d.textContent).join(' ')")
[ "$clock" = "2 2" ] || fail "after a double click on Next cycle the week and cycle read '$clock', expected '2 2'."
[ "$(phase)" = "Orders" ] || fail "a double click on Next cycle left the page in $(phase), not Orders."

console=$(pw --raw console)
case "$console" in
  *"Errors: 0,"*) ;;
  *) fail "the browser console has errors: $console" ;;
esac

echo "Browser: one cycle played, double clicks held. OK."
