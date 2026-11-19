#!/usr/bin/env bash
# Isolated nopCommerce verification instances: build a copy of the working tree,
# run it on its own port against its own PostgreSQL database, install the store
# with sample data, and drive it with Playwright.
#
# Usage: nopverify.sh <command> [args]   (see `nopverify.sh help`)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
BASE="${NOPVERIFY_BASE:-/tmp/nop-verify}"
NAME="${NOPVERIFY_NAME:-default}"
PORT="${NOPVERIFY_PORT:-8090}"
PYLIB="$BASE/.pylib"

if [ -z "${NOPVERIFY_EVIDENCE:-}" ]; then
  if [ -d /opt/cursor/artifacts ] && [ -w /opt/cursor/artifacts ]; then
    NOPVERIFY_EVIDENCE=/opt/cursor/artifacts/verify-nopcommerce
  else
    NOPVERIFY_EVIDENCE="${TMPDIR:-/tmp}/nop-verify-evidence"
  fi
fi

PG_HOST=localhost
PG_USER=nopcommerce
PG_PASSWORD=nopCommerce_db_password
ADMIN_EMAIL="${NOPVERIFY_ADMIN_EMAIL:-admin@yourstore.com}"
ADMIN_PASSWORD="${NOPVERIFY_ADMIN_PASSWORD:-Admin123!}"

export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/share/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

log() { printf '[nopverify %s] %s\n' "$(date -u +%H:%M:%S)" "$*" >&2; }
die() { log "ERROR: $*"; exit 1; }

set_instance() {
  [[ "$NAME" =~ ^[a-z0-9_]+$ ]] || die "instance name must match [a-z0-9_]+ (got '$NAME')"
  ROOT="$BASE/$NAME"
  STATE="$ROOT/instance.env"
  DB="nopverify_$NAME"
  SESSION="nopverify-$NAME"
  if [ -f "$STATE" ]; then
    # shellcheck disable=SC1090
    . "$STATE"
    PORT="${NOPVERIFY_PORT:-$PORT}"
  fi
  URL="http://127.0.0.1:$PORT"
}

write_state() {
  mkdir -p "$ROOT"
  cat >"$STATE" <<EOF
NAME=$NAME
PORT=$PORT
DB=$DB
SESSION=$SESSION
URL=http://127.0.0.1:$PORT
FINGERPRINT=${FINGERPRINT:-}
EOF
}

psql_db() { PGPASSWORD="$PG_PASSWORD" psql -h "$PG_HOST" -U "$PG_USER" -v ON_ERROR_STOP=1 "$@"; }

fingerprint() {
  # HEAD plus every uncommitted/untracked change under src/: changes when the code under test changes.
  (
    cd "$REPO"
    git rev-parse HEAD
    git diff HEAD -- src
    git ls-files -z -o --exclude-standard -- src | xargs -0 -r sha1sum
  ) | sha1sum | cut -c1-12
}

dotnet_pid() { [ -f "$ROOT/dotnet.pid" ] && cat "$ROOT/dotnet.pid" || true; }
pid_alive() { [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null; }
port_owner() { lsof -nP -iTCP:"$1" -sTCP:LISTEN -t 2>/dev/null | head -1 || true; }
http_code() { curl -s -o /dev/null -w '%{http_code}' --max-time "${2:-10}" "$1" || true; }

ensure_postgres() {
  if ! pg_isready -q -h "$PG_HOST"; then
    log "starting PostgreSQL"
    sudo service postgresql start >/dev/null
    for _ in $(seq 1 30); do pg_isready -q -h "$PG_HOST" && break; sleep 1; done
    pg_isready -q -h "$PG_HOST" || die "PostgreSQL did not become ready"
  fi
  if ! psql_db -d postgres -Atc 'select 1' >/dev/null 2>&1; then
    log "creating PostgreSQL role $PG_USER"
    sudo -u postgres psql -v ON_ERROR_STOP=1 -q <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$PG_USER') THEN
    CREATE ROLE $PG_USER LOGIN PASSWORD '$PG_PASSWORD' CREATEDB;
  ELSE
    ALTER ROLE $PG_USER WITH LOGIN PASSWORD '$PG_PASSWORD' CREATEDB;
  END IF;
END
\$\$;
SQL
  fi
}

sync_source() {
  log "copying working tree src/ -> $ROOT/src"
  mkdir -p "$ROOT/src"
  local manifest="$ROOT/src.manifest" new="$ROOT/src.manifest.new"
  (cd "$REPO" && git ls-files -co --exclude-standard -- src | while IFS= read -r f; do [ -e "$f" ] && printf '%s\n' "$f"; done) >"$new"
  if [ -f "$manifest" ]; then
    comm -23 <(sort "$manifest") <(sort "$new") | while IFS= read -r f; do rm -f "$ROOT/$f"; done
  fi
  (cd "$REPO" && tr '\n' '\0' <"$new" | tar --null -T - -cf -) | tar -xpf - -C "$ROOT"
  mv "$new" "$manifest"
}

build_and_publish() {
  mkdir -p "$ROOT/logs"
  log "building NopCommerce.sln (Release) -> $ROOT/logs/build.log"
  if ! (cd "$ROOT/src" && dotnet build NopCommerce.sln -c Release -nologo -clp:ErrorsOnly >"$ROOT/logs/build.log" 2>&1); then
    tail -40 "$ROOT/logs/build.log" >&2
    die "build failed (full log: $ROOT/logs/build.log)"
  fi
  log "publishing Nop.Web -> $ROOT/app"
  if ! (cd "$ROOT/src/Presentation/Nop.Web" && dotnet publish Nop.Web.csproj -c Release --no-build -nologo -o "$ROOT/app" >>"$ROOT/logs/build.log" 2>&1); then
    tail -40 "$ROOT/logs/build.log" >&2
    die "publish failed (full log: $ROOT/logs/build.log)"
  fi
  mkdir -p "$ROOT/app/logs" "$ROOT/app/bin"
}

start_app() {
  local owner
  owner="$(port_owner "$PORT")"
  [ -z "$owner" ] || die "port $PORT is already in use by pid $owner (not this instance). Pick another: NOPVERIFY_PORT=<port>"
  rm -f "$ROOT/stop"
  cat >"$ROOT/run-loop.sh" <<EOF
#!/usr/bin/env bash
# nopCommerce stops its own process after install and plugin changes; restart it until asked to stop.
export DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:\$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ASPNETCORE_ENVIRONMENT="${NOPVERIFY_ASPNETCORE_ENVIRONMENT:-Development}"
cd "$ROOT/app"
while [ ! -f "$ROOT/stop" ]; do
  dotnet Nop.Web.dll --urls "http://127.0.0.1:$PORT" >>"$ROOT/logs/app.log" 2>&1 &
  echo \$! >"$ROOT/dotnet.pid"
  wait \$! || true
  echo "--- app exited at \$(date -u +%FT%TZ) ---" >>"$ROOT/logs/app.log"
  sleep 1
done
rm -f "$ROOT/dotnet.pid"
EOF
  chmod +x "$ROOT/run-loop.sh"
  log "starting app in tmux session $SESSION on $URL (log: $ROOT/logs/app.log)"
  tmux new-session -d -s "$SESSION" "$ROOT/run-loop.sh"
}

wait_http() {
  # Wait until / answers 200 (installed) or 302 (redirect to /install).
  local timeout="${1:-240}" code
  for _ in $(seq 1 "$timeout"); do
    code="$(http_code "$URL/" 5)"
    case "$code" in 200|302) echo "$code"; return 0 ;; esac
    sleep 1
  done
  tail -30 "$ROOT/logs/app.log" >&2 || true
  die "app did not answer on $URL within ${timeout}s"
}

# nopCommerce stores the install's connection string in App_Data/appsettings.json (ConnectionStrings section).
is_installed() {
  [ -s "$ROOT/app/App_Data/appsettings.json" ] && python3 - "$ROOT/app/App_Data/appsettings.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as f:
    sys.exit(0 if (json.load(f).get("ConnectionStrings") or {}).get("ConnectionString") else 1)
PY
}

install_store() {
  log "installing store via /install (PostgreSQL db $DB, sample data, admin $ADMIN_EMAIL)"
  local jar="$ROOT/install.cookies" page="$ROOT/logs/install-get.html" out="$ROOT/logs/install-post.html" token
  curl -s -c "$jar" -b "$jar" --max-time 60 "$URL/install" -o "$page"
  token="$(grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' "$page" | head -1 | sed 's/.*value="//; s/"$//')"
  [ -n "$token" ] || die "no antiforgery token on /install (see $page)"
  curl -s -c "$jar" -b "$jar" --max-time 1200 -o "$out" -X POST "$URL/install" \
    --data-urlencode "__RequestVerificationToken=$token" \
    --data-urlencode "AdminEmail=$ADMIN_EMAIL" \
    --data-urlencode "AdminPassword=$ADMIN_PASSWORD" \
    --data-urlencode "ConfirmPassword=$ADMIN_PASSWORD" \
    --data-urlencode "Country=US-en-US" \
    --data-urlencode "InstallSampleData=true" \
    --data-urlencode "SubscribeNewsletters=false" \
    --data-urlencode "DataProvider=PostgreSQL" \
    --data-urlencode "CreateDatabaseIfNotExists=true" \
    --data-urlencode "ConnectionStringRaw=false" \
    --data-urlencode "ServerName=$PG_HOST" \
    --data-urlencode "DatabaseName=$DB" \
    --data-urlencode "Username=$PG_USER" \
    --data-urlencode "Password=$PG_PASSWORD"
  if ! grep -q 'restartapplication' "$out"; then
    grep -o 'validation-summary-errors.*' "$out" | sed 's/<[^>]*>/ /g' | head -5 >&2 || true
    die "installation did not complete (response: $out, app log: $ROOT/logs/app.log)"
  fi
  log "install finished; restarting app so it installs plugins"
  curl -s -o /dev/null --max-time 30 -b "$jar" "$URL/install/restartapplication" || true
  local old_pid; old_pid="$(dotnet_pid)"
  for _ in $(seq 1 60); do [ "$(dotnet_pid)" != "$old_pid" ] && break; sleep 1; done
}

wait_store_ready() {
  # Ready = storefront renders sample data, not just a 200 from Kestrel.
  local timeout="${1:-300}"
  for _ in $(seq 1 "$timeout"); do
    if curl -s --max-time 20 "$URL/" | grep -q 'Build your own computer'; then return 0; fi
    sleep 2
  done
  tail -30 "$ROOT/logs/app.log" >&2 || true
  die "storefront never rendered sample products on $URL"
}

cmd_launch() {
  local fresh=0 skip_build=0 no_install=0
  for a in "$@"; do
    case "$a" in
      --fresh) fresh=1 ;;
      --skip-build) skip_build=1 ;;
      --no-install) no_install=1 ;;
      *) die "unknown launch flag: $a" ;;
    esac
  done
  for t in dotnet tmux psql pg_isready curl lsof tar git; do command -v "$t" >/dev/null || die "missing prerequisite: $t"; done
  ensure_postgres
  if [ -f "$STATE" ] || tmux has-session -t "=$SESSION" 2>/dev/null; then cmd_stop quiet; fi
  if [ "$fresh" = 1 ]; then
    log "--fresh: dropping database $DB and published app"
    psql_db -d postgres -qc "DROP DATABASE IF EXISTS $DB WITH (FORCE)" 2>/dev/null || true
    rm -rf "$ROOT/app" "$ROOT/install.cookies"
  fi
  if is_installed && ! psql_db -d "$DB" -Atc 'select 1' >/dev/null 2>&1; then
    log "App_Data/appsettings.json points at a missing database; resetting install state"
    rm -f "$ROOT/app/App_Data/appsettings.json" "$ROOT/app/App_Data/plugins.json"
  fi
  FINGERPRINT="$(fingerprint)"
  if [ "$skip_build" = 1 ] && [ -f "$ROOT/app/Nop.Web.dll" ]; then
    log "--skip-build: reusing $ROOT/app"
    # shellcheck disable=SC1090
    FINGERPRINT="$( [ -f "$STATE" ] && . "$STATE" && echo "$FINGERPRINT" || echo unknown)"
  else
    sync_source
    build_and_publish
  fi
  write_state
  start_app
  local code; code="$(wait_http 240)"
  if [ "$no_install" = 1 ] && ! is_installed; then
    mkdir -p "$NOPVERIFY_EVIDENCE"
    log "READY (not installed, --no-install): $URL/install  (use db name $DB, server $PG_HOST, user $PG_USER / $PG_PASSWORD)"
    return 0
  fi
  if ! is_installed; then
    [ "$code" = 302 ] || log "warning: expected 302 to /install on an uninstalled app, got $code"
    install_store
    wait_http 240 >/dev/null
  fi
  wait_store_ready 300
  mkdir -p "$NOPVERIFY_EVIDENCE"
  log "READY: $URL  (admin: $ADMIN_EMAIL / $ADMIN_PASSWORD, db: $DB, evidence: $NOPVERIFY_EVIDENCE)"
}

cmd_doctor() {
  local ok=1 pid owner code products cur
  echo "instance:     $NAME ($ROOT)"
  if [ ! -f "$STATE" ]; then echo "FAIL  no instance state at $STATE - run: nopverify.sh launch"; return 1; fi
  echo "url:          $URL"
  if tmux has-session -t "=$SESSION" 2>/dev/null; then echo "ok    tmux session $SESSION"; else echo "FAIL  tmux session $SESSION missing"; ok=0; fi
  pid="$(dotnet_pid)"
  if pid_alive "$pid"; then echo "ok    dotnet pid $pid alive"; else echo "FAIL  dotnet process not running (pid file: '${pid:-none}')"; ok=0; fi
  owner="$(port_owner "$PORT")"
  if [ -n "$owner" ] && [ "$owner" = "$pid" ]; then echo "ok    port $PORT owned by our pid"; elif [ -n "$owner" ]; then echo "FAIL  port $PORT owned by pid $owner, not ours ($pid)"; ok=0; else echo "FAIL  nothing listening on $PORT"; ok=0; fi
  code="$(http_code "$URL/" 15)"
  if [ "$code" = 200 ]; then echo "ok    GET / -> 200"; else echo "FAIL  GET / -> $code (302 means not installed)"; ok=0; fi
  if is_installed; then echo "ok    installed (connection string in app/App_Data/appsettings.json)"; else echo "FAIL  not installed"; ok=0; fi
  products="$(psql_db -d "$DB" -Atc 'select count(*) from "Product" where "Deleted" = false' 2>/dev/null || echo '?')"
  if [[ "$products" =~ ^[0-9]+$ ]] && [ "$products" -gt 0 ]; then echo "ok    db $DB reachable, $products products"; else echo "FAIL  db $DB products query: $products"; ok=0; fi
  cur="$(fingerprint)"
  if [ "${FINGERPRINT:-}" = "$cur" ]; then echo "ok    build matches working tree ($cur)"; else echo "WARN  build is stale: built ${FINGERPRINT:-unknown}, working tree now $cur - relaunch to test current code"; fi
  echo "admin:        $ADMIN_EMAIL / $ADMIN_PASSWORD"
  echo "logs:         $ROOT/logs/app.log"
  echo "evidence dir: $NOPVERIFY_EVIDENCE"
  [ "$ok" = 1 ] && echo "HEALTHY" || { echo "UNHEALTHY"; return 1; }
}

cmd_stop() {
  local quiet="${1:-}" pid
  touch "$ROOT/stop" 2>/dev/null || true
  pid="$(dotnet_pid)"
  if pid_alive "$pid"; then
    kill "$pid" 2>/dev/null || true
    for _ in $(seq 1 20); do pid_alive "$pid" || break; sleep 0.5; done
    pid_alive "$pid" && kill -9 "$pid" 2>/dev/null || true
  fi
  tmux kill-session -t "=$SESSION" 2>/dev/null || true
  rm -f "$ROOT/dotnet.pid"
  if [ -n "$(port_owner "$PORT")" ] && [ "$(port_owner "$PORT")" = "$pid" ]; then die "pid $pid still owns port $PORT"; fi
  [ -n "$quiet" ] || log "stopped instance $NAME (data kept in $ROOT and db $DB)"
}

cmd_cleanup() {
  cmd_stop quiet
  psql_db -d postgres -qc "DROP DATABASE IF EXISTS $DB WITH (FORCE)" || log "warning: could not drop $DB"
  case "$ROOT" in "$BASE"/?*) rm -rf "$ROOT" ;; *) die "refusing to delete unexpected path $ROOT" ;; esac
  log "removed instance $NAME (dir, tmux session, db $DB). Evidence kept in $NOPVERIFY_EVIDENCE"
}

harness_ready() { PYTHONPATH="$PYLIB" python3 -c 'import playwright' 2>/dev/null && compgen -G "$HOME/.cache/ms-playwright/ffmpeg-*/ffmpeg-linux" >/dev/null; }

cmd_harness() {
  if harness_ready; then log "playwright + ffmpeg already installed ($PYLIB)"; return 0; fi
  log "installing playwright (python) into $PYLIB; it drives the system Google Chrome, only ffmpeg (for video) is downloaded"
  mkdir -p "$PYLIB"
  PYTHONPATH="$PYLIB" python3 -c 'import playwright' 2>/dev/null || pip3 install -q --target "$PYLIB" playwright
  PYTHONPATH="$PYLIB" python3 -m playwright install ffmpeg
  command -v google-chrome >/dev/null || log "warning: google-chrome not found; run: PYTHONPATH=$PYLIB python3 -m playwright install chromium and set NOPVERIFY_BROWSER_CHANNEL=chromium"
}

cmd_py() {
  [ $# -ge 1 ] || die "usage: nopverify.sh py <script.py> [args]"
  harness_ready || cmd_harness
  mkdir -p "$NOPVERIFY_EVIDENCE"
  NOPVERIFY_URL="$URL" NOPVERIFY_DB="$DB" NOPVERIFY_EVIDENCE="$NOPVERIFY_EVIDENCE" \
    NOPVERIFY_ADMIN_EMAIL="$ADMIN_EMAIL" NOPVERIFY_ADMIN_PASSWORD="$ADMIN_PASSWORD" \
    PYTHONDONTWRITEBYTECODE=1 PYTHONPATH="$SCRIPT_DIR:$PYLIB" python3 "$@"
}

cmd_psql() { psql_db -d "$DB" "$@"; }

cmd_help() {
  sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'
  cat <<EOF

Commands (instance from NOPVERIFY_NAME, default 'default'; port NOPVERIFY_PORT, default 8090):
  launch [--fresh] [--skip-build] [--no-install]
                                   copy working tree, build, publish, start, install sample store, wait until usable
                                   (--no-install stops at the /install wizard so the installer UI can be driven)
  doctor                           read-only health check of the instance
  url                              print the instance base URL
  harness                          install Playwright (python) into $PYLIB
  py <script.py> [args]            run a Playwright script with NOPVERIFY_* env and nopdrive importable
  psql [psql args]                 psql into the instance database, e.g. psql -c 'select count(*) from "Order"'
  logs                             tail the app log
  stop                             stop the app, keep build, database and install state
  cleanup                          stop, drop the database, delete $BASE/<name>; never touches evidence
Evidence dir: $NOPVERIFY_EVIDENCE
EOF
}

set_instance
cmd="${1:-help}"; shift || true
case "$cmd" in
  launch) cmd_launch "$@" ;;
  doctor) cmd_doctor ;;
  url) echo "$URL" ;;
  harness) cmd_harness ;;
  py) cmd_py "$@" ;;
  psql) cmd_psql "$@" ;;
  logs) tail -n "${1:-80}" "$ROOT/logs/app.log" ;;
  stop) cmd_stop ;;
  cleanup) cmd_cleanup ;;
  help|-h|--help) cmd_help ;;
  *) die "unknown command '$cmd' (try: help)" ;;
esac
