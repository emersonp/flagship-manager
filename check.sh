#!/usr/bin/env bash
# The gate every change must pass: build, tests, a headless simulator run, and a mission bench, all pinned to the
# placeholder pack in tests/fixtures/content; one more run only proves the game's own content/ plays (exit code only).
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content tests/fixtures/content
dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --team sweep --content tests/fixtures/content > /dev/null
echo "Mission bench: 3 x 2000 missions. OK."
game=$(dotnet run --no-build --project src/SimRunner -- cycles --legacies 3 --cycles 5 --seed 1 --content content) || { echo "$game" >&2; exit 1; }
echo "Game content: $game"

# Web smoke check: scratch database and a scratch copy of tests/fixtures/content/, free port, everything removed on exit.
port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')
tmp=$(mktemp -d)
base="http://127.0.0.1:$port"
app_pid=""
stop_app() {
  if [ -n "$app_pid" ]; then
    # The app runs in its own process group (setsid), so one signal reaches dotnet run and its child.
    kill -TERM -- "-$app_pid" 2>/dev/null || true
    for _ in $(seq 1 50); do
      kill -0 -- "-$app_pid" 2>/dev/null || break
      sleep 0.1
    done
    kill -KILL -- "-$app_pid" 2>/dev/null || true
    app_pid=""
  fi
}
cleanup() {
  stop_app
  rm -rf "$tmp"
}
trap cleanup EXIT
web_fail() {
  echo "Web: $1" >&2
  echo "--- app log ---" >&2
  cat "$tmp/app.log" >&2 || true
  exit 1
}

# The editor writes the files it reads, so the app reads a copy of the fixture, never the repository's folders; checksums prove it.
content_sum() {
  (cd "$1" && find . -type f | LC_ALL=C sort | xargs sha256sum | sha256sum)
}
content_before=$(content_sum content)
fixture_before=$(content_sum tests/fixtures/content)
cp -r tests/fixtures/content "$tmp/content"

# Starts the app with developer mode $1 (true or false) and waits until it answers.
start_app() {
  ASPNETCORE_URLS="$base" Web__DatabasePath="$tmp/check-$1.db" Web__ContentDirectory="$tmp/content" Web__BeatDelayMs=0 Web__DeveloperMode="$1" ASPNETCORE_ENVIRONMENT=Production \
    setsid dotnet run --no-build --project src/Web > "$tmp/app.log" 2>&1 &
  app_pid=$!
  local ready=""
  for _ in $(seq 1 60); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/" || true)" = "200" ]; then
      ready=1
      break
    fi
    sleep 0.5
  done
  [ -n "$ready" ] || web_fail "GET / never answered 200 within 30 seconds (developer mode $1)."
}

start_app true

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

# The editor: the index lists CREW_01, and the crew form names it. The post carries every field the form holds, as a browser would.
status=$(curl -s -o "$tmp/index.html" -w '%{http_code}' "$base/dev/content" || true)
[ "$status" = "200" ] || web_fail "GET /dev/content answered $status with developer mode on, expected 200."
grep -q "CREW_01" "$tmp/index.html" || web_fail "GET /dev/content did not list CREW_01."
status=$(curl -s -o "$tmp/crew.html" -w '%{http_code}' "$base/dev/content/crew/CREW_01" || true)
[ "$status" = "200" ] || web_fail "GET /dev/content/crew/CREW_01 answered $status, expected 200."
python3 - "$tmp/crew.html" "$tmp/crew.fields" <<'PY'
import html.parser, sys, urllib.parse
class Form(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.inside, self.fields = False, []
    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "form" and a.get("id") == "item-form":
            self.inside = True
        elif self.inside and tag == "input" and a.get("name") and a.get("type") not in ("checkbox", "radio"):
            self.fields.append((a["name"], a.get("value", "")))
    def handle_endtag(self, tag):
        if tag == "form":
            self.inside = False
parser = Form()
parser.feed(open(sys.argv[1], encoding="utf-8").read())
fields = [(k, "NAME_PLACEHOLDER" if k == "name" else v) for k, v in parser.fields]
assert any(k == "name" for k, _ in fields) and any(k.startswith("skills.") for k, _ in fields), fields
open(sys.argv[2], "w").write(urllib.parse.urlencode(fields))
PY
status=$(curl -s -o /dev/null -w '%{http_code}' --data @"$tmp/crew.fields" "$base/dev/content/crew/CREW_01" || true)
[ "$status" = "200" ] || web_fail "POST /dev/content/crew/CREW_01 answered $status, expected 200."
grep -q "NAME_PLACEHOLDER" "$tmp/content/crew/starting.yaml" || web_fail "The crew file in the scratch copy does not hold the new name."
status=$(curl -s -o "$tmp/legacy.html" -w '%{http_code}' "$base$location" || true)
[ "$status" = "200" ] || web_fail "GET $location answered $status after the edit, expected 200."
grep -q "NAME_PLACEHOLDER" "$tmp/legacy.html" || web_fail "The legacy's page does not show the new name."

# With developer mode off the editor does not exist.
stop_app
start_app false
status=$(curl -s -o /dev/null -w '%{http_code}' "$base/dev/content" || true)
[ "$status" = "404" ] || web_fail "GET /dev/content answered $status with developer mode off, expected 404."
[ "$(content_sum content)" = "$content_before" ] || web_fail "The check changed the repository's content/."
[ "$(content_sum tests/fixtures/content)" = "$fixture_before" ] || web_fail "The check changed tests/fixtures/content/."
echo "Web: legacy created and opened, and content edited. OK."
