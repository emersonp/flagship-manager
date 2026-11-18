# Sourced by check.sh and tests/browser/*.sh: starts the built web app and stops it, the same on Linux, macOS, and Windows
# (Git Bash). It runs Web.dll itself rather than dotnet run, so the app is one process that a plain kill stops (no process
# group, no setsid), and it binds port 0, so the app picks a free port and names it in its log (no helper to find one).
# The caller sets root to the repository and builds src/Web first. web_start sets web_base and web_pid.

web_pid=""
web_base=""

# The interpreter that runs Python 3, by whichever name this machine gives it (python3 on Linux and macOS, often python on
# Windows, where python3 may be the store's stand-in that runs nothing).
web_python() {
  local name
  for name in python3 python; do
    if command -v "$name" > /dev/null 2>&1 && "$name" -c 'import sys; sys.exit(sys.version_info[0] != 3)' > /dev/null 2>&1; then
      echo "$name"
      return 0
    fi
  done
  echo "Python 3 was not found as python3 or python." >&2
  return 1
}

# Starts the app in production with the settings given as NAME=value arguments after the log file, and waits up to 30 seconds
# until it names its address and GET / answers 200. Returns 1, with the reason on stderr, if it does not.
web_start() {
  local log="$1"
  shift
  local framework dll
  framework=$(sed -n 's:.*<TargetFramework>\(.*\)</TargetFramework>.*:\1:p' "$root/Directory.Build.props")
  dll="$root/src/Web/bin/Debug/$framework/Web.dll"
  if [ ! -f "$dll" ]; then
    echo "$dll is not built." >&2
    return 1
  fi

  (
    for setting in "$@"; do
      export "${setting?}"
    done
    export ASPNETCORE_URLS="http://127.0.0.1:0" ASPNETCORE_ENVIRONMENT=Production
    exec dotnet "$dll" --contentRoot "$root/src/Web"
  ) > "$log" 2>&1 &
  web_pid=$!

  web_base=""
  for _ in $(seq 1 60); do
    web_base=$(sed -n 's/.*Now listening on: \(http[^[:space:]]*\).*/\1/p' "$log" | head -n 1 | tr -d '\r')
    [ -n "$web_base" ] && break
    kill -0 "$web_pid" 2> /dev/null || { echo "The app stopped before it was listening." >&2; return 1; }
    sleep 0.5
  done
  if [ -z "$web_base" ]; then
    echo "The app did not name its address within 30 seconds." >&2
    return 1
  fi

  for _ in $(seq 1 60); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "$web_base/" || true)" = "200" ]; then
      return 0
    fi
    sleep 0.5
  done
  echo "GET / never answered 200 within 30 seconds." >&2
  return 1
}

# Stops the app if it runs: asks, waits up to 5 seconds, then forces it.
web_stop() {
  if [ -n "$web_pid" ]; then
    kill -TERM "$web_pid" 2> /dev/null || true
    for _ in $(seq 1 50); do
      kill -0 "$web_pid" 2> /dev/null || break
      sleep 0.1
    done
    kill -KILL "$web_pid" 2> /dev/null || true
    wait "$web_pid" 2> /dev/null || true
    web_pid=""
    web_base=""
  fi
}
