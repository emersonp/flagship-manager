#!/usr/bin/env bash
# The gate every change must pass: build, tests, a headless simulator run, and a mission bench.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content
dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --team sweep --content content > /dev/null
echo "Mission bench: 3 x 2000 missions. OK."

# Web smoke check: scratch database, free port, everything removed on exit.
port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')
tmp=$(mktemp -d)
base="http://127.0.0.1:$port"
app_pid=""
cleanup() {
  if [ -n "$app_pid" ]; then
    # The app runs in its own process group (setsid), so one signal reaches dotnet run and its child.
    kill -TERM -- "-$app_pid" 2>/dev/null || true
    for _ in $(seq 1 50); do
      kill -0 -- "-$app_pid" 2>/dev/null || break
      sleep 0.1
    done
    kill -KILL -- "-$app_pid" 2>/dev/null || true
  fi
  rm -rf "$tmp"
}
trap cleanup EXIT
web_fail() {
  echo "Web: $1" >&2
  echo "--- app log ---" >&2
  cat "$tmp/app.log" >&2 || true
  exit 1
}

ASPNETCORE_URLS="$base" Web__DatabasePath="$tmp/check.db" Web__BeatDelayMs=0 ASPNETCORE_ENVIRONMENT=Production \
  setsid dotnet run --no-build --project src/Web > "$tmp/app.log" 2>&1 &
app_pid=$!

ready=""
for _ in $(seq 1 60); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/" || true)" = "200" ]; then
    ready=1
    break
  fi
  sleep 0.5
done
[ -n "$ready" ] || web_fail "GET / never answered 200 within 30 seconds."

curl -s -o /dev/null -D "$tmp/post.headers" -w '%{http_code}' --data-urlencode "name=CHECK" "$base/legacies" > "$tmp/post.status" || true
status=$(cat "$tmp/post.status")
[ "$status" = "303" ] || web_fail "POST /legacies answered $status, expected 303."
location=$(awk 'tolower($1)=="location:" {print $2}' "$tmp/post.headers" | tr -d '\r')
case "$location" in
  /legacy/*) ;;
  *) web_fail "POST /legacies redirected to '$location', expected /legacy/{id}." ;;
esac

status=$(curl -s -o "$tmp/legacy.html" -w '%{http_code}' "$base$location" || true)
[ "$status" = "200" ] || web_fail "GET $location answered $status, expected 200."
grep -q "CHECK" "$tmp/legacy.html" || web_fail "GET $location did not contain CHECK."

for asset in /lib/datastar.js /css/app.css; do
  status=$(curl -s -o /dev/null -w '%{http_code}' "$base$asset" || true)
  [ "$status" = "200" ] || web_fail "GET $asset answered $status, expected 200."
done
echo "Web: legacy created and opened. OK."
