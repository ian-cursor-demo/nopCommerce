---
name: verify-nopcommerce
description: Launch an isolated nopCommerce storefront + admin (built from the current working tree, PostgreSQL, sample data) and drive it with Playwright to prove a change works, capturing screenshots, video, trace and database side effects. Use when verifying any user-facing nopCommerce change (storefront, checkout, customer account, admin area, installer) end to end, not for unit tests.
---

# Verify nopCommerce

nopCommerce is an ASP.NET Core (.NET 10) e-commerce app. Users touch two web surfaces served by one process (`src/Presentation/Nop.Web`):

- the **public storefront** (`/`, catalog, cart, checkout, customer account), and
- the **admin area** (`/Admin`, same login as customers, Administrators role).

There is also a first-run **install wizard** (`/install`). This skill drives all of them through a real browser.

Everything lives in `scripts/` next to this file. Run commands from the repo root:

```bash
V=.cursor/skills/verify-nopcommerce/scripts
$V/nopverify.sh help
```

## Isolation: never drive the shared instance

The VM's start script (`/tmp/cursor/start-user/start-user.sh`) runs `dotnet run` straight from the checkout on **port 8080**. That instance keeps its install state in the checkout's ignored `src/Presentation/Nop.Web/App_Data/appsettings.json` and `plugins.json`. Plugins also build into the shared `src/Presentation/Nop.Web/Plugins/` folder no matter the configuration. Installing it, or building the solution in the checkout while it runs, changes the user's instance. **Do not drive port 8080 and do not run `dotnet build` on the solution in the checkout for verification.**

`nopverify.sh` gives each verification instance its own copy:

| What | Where |
| --- | --- |
| Source copy (`git ls-files` of `src/`, including uncommitted and untracked files) | `/tmp/nop-verify/<name>/src` |
| Release build + publish | `/tmp/nop-verify/<name>/app` (install state in `app/App_Data/appsettings.json`) |
| Database | PostgreSQL `nopverify_<name>` (role `nopcommerce` / `nopCommerce_db_password`) |
| Process | tmux session `nopverify-<name>`, restart loop, pid in `/tmp/nop-verify/<name>/dotnet.pid` |
| URL | `http://127.0.0.1:<port>`, default 8090 |
| Logs | `/tmp/nop-verify/<name>/logs/{build,app}.log` |

Instances run side by side when you give each a `NOPVERIFY_NAME` (`[a-z0-9_]+`) and a `NOPVERIFY_PORT`. Launch refuses a port held by another process.

## Launch

```bash
$V/nopverify.sh launch            # instance "default" on :8090
NOPVERIFY_NAME=pr123 NOPVERIFY_PORT=8092 $V/nopverify.sh launch   # a second, independent instance
```

What it does, in order:

1. Starts PostgreSQL and creates the `nopcommerce` role if missing. This uses `sudo`.
2. Copies the working tree's `src/` into the instance directory.
3. Runs `dotnet build NopCommerce.sln -c Release`, then `dotnet publish Nop.Web`. The first build takes about a minute; later launches are incremental.
4. Starts the app in tmux.
5. If the app isn't installed yet, POSTs the real `/install` form with:
   - PostgreSQL, creating the database;
   - **sample data**;
   - admin `admin@yourstore.com` / `Admin123!`;
   - newsletter off and country `US-en-US`, so the install makes no calls to nopcommerce.com.
6. Calls `/install/restartapplication` so nopCommerce restarts and installs its plugins.

Launch is **ready** only when it prints `READY: http://127.0.0.1:<port>`. That means the storefront HTML contains the sample product "Build your own computer", not just that Kestrel answers. From an empty state this takes about 2 minutes.

Flags:

- `--fresh` drops the database and the published app, then reinstalls. Use it when a test needs pristine sample data.
- `--skip-build` restarts the existing publish without copying or building.
- `--no-install` stops at the `/install` wizard, so the installer UI can be driven (see `features/install-wizard.md`).

**After every code change, run `launch` again.** It stops the instance, recopies the source, does an incremental rebuild, and restarts while keeping the database. Views are precompiled in Release, so a `.cshtml` change also needs a relaunch. `doctor` warns when the build is stale.

Set `NOPVERIFY_ASPNETCORE_ENVIRONMENT=Production` to test production error pages. The default is `Development`, which shows the developer exception page.

## Doctor

Run this read-only check first, and whenever anything looks off:

```bash
$V/nopverify.sh doctor     # exits non-zero unless HEALTHY
```

It checks:

- the tmux session exists and the dotnet pid is alive;
- that pid owns the port;
- `GET /` returns 200;
- the install connection string is present;
- the database has products;
- the build fingerprint (HEAD plus the diff and untracked files under `src/`) matches the working tree. A mismatch prints `WARN build is stale`; relaunch before trusting results.

On a `--no-install` instance, doctor reports FAIL for GET / (302), installed state and the database. That is expected until the wizard has run.

`$V/nopverify.sh logs [N]` tails the app log. Unhandled exceptions also go to the `"Log"` table.

## Drive

The harness is Playwright for Python, driving the system Google Chrome (`channel="chrome"`, headless). `nopverify.sh harness` installs it into `/tmp/nop-verify/.pylib` with `pip --target` (the system Python has no `venv`/`ensurepip`) and downloads only Playwright's ffmpeg for video. `nopverify.sh py` installs it on first use.

```bash
$V/nopverify.sh py $V/journeys/guest_checkout.py                       # proven end-to-end journey
NOPVERIFY_NAME=installer $V/nopverify.sh py $V/journeys/install_wizard.py
$V/nopverify.sh py /tmp/my_journey.py                                  # your own journey
```

`py` sets `NOPVERIFY_URL`, `NOPVERIFY_DB`, `NOPVERIFY_EVIDENCE` and the admin credentials, and puts `scripts/nopdrive.py` on `PYTHONPATH`. Write journeys like this:

```python
from nopdrive import Run, expect

with Run("my-feature") as run:          # opens Chrome, starts trace + video, creates the evidence folder
    page = run.page                     # Playwright Page; page.goto("/path") is relative to the instance
    run.goto("/")                       # logs status + final URL
    run.shot("home")                    # numbered screenshot in the evidence folder
    run.check(cond, "what this proves") # recorded in result.json; raises on failure
    run.db('select count(*) from "Order"')  # side-effect query against this instance's DB
    run.wait_ajax()                     # wait for jQuery AJAX (add to cart, one-page checkout, admin grids)
    run.login(email, pw); run.admin_login()
```

A journey fails, and `py` exits 1, on:

- an exception;
- a failed `check`;
- **any HTTP 5xx** response seen by the page.

Browser console errors are recorded in `result.json` but don't fail the run.

Stable handles in this app:

- Element ids from `asp-for` (`#Email`, `#BillingNewAddress_FirstName`, `#SearchProductName`).
- Semantic button classes (`button.login-button`, `.new-address-next-step-button`, `.confirm-order-next-step-button`).
- Header link classes (`.ico-login`, `.ico-logout`, `.ico-cart`, `.ico-account`).
- Admin grid ids (`#products-grid`, `#orders-grid`, `#customers-grid`).

Prefer these over text, which is localized, and over coordinates. Exact handles per feature are in `features/`.

For a quick manual look, open the instance URL in the Cursor browser or a `computerUse` subagent. Scripted journeys are the proof.

## Evidence

Each `Run` writes `$NOPVERIFY_EVIDENCE/<UTC stamp>-<journey>/`:

- `NN-<label>.png` screenshots, in order;
- `steps.md`, a timestamped action log including DB queries and results;
- `result.json` with passed/failed, every check, server errors and console errors;
- `trace.zip`, a Playwright trace. Open it with `PYTHONPATH=/tmp/nop-verify/.pylib python3 -m playwright show-trace <zip>`;
- `video/*.webm`.

`NOPVERIFY_EVIDENCE` defaults to `/opt/cursor/artifacts/verify-nopcommerce` when `/opt/cursor/artifacts` is writable (cloud agents), else `/tmp/nop-verify-evidence`. It sits outside the instance directory on purpose, so cleanup never deletes it.

Proof standards:

- Drive the real user path: header search, buttons, forms. Don't call internal endpoints or set state in SQL to reach a step. `run.db` is for **reading** side effects. Writing to the DB is allowed only to set up a precondition the UI can't reach, and the journey must say so in `run.log`.
- Capture the action and the resulting state: a screenshot before the submit and after it, not just the final page.
- Verify side effects next to what is visible. For example: an order row with the right email, item and total; a customer row; a setting value. The UI saying "success" is not enough.
- No mocks. The only external systems touched during install (nopcommerce.com newsletter and language packs) are switched off by the install inputs. Payment uses the offline Check / Money Order plugin. PayPal and other live gateways can't be proven here; say so instead of faking them.
- When a journey fails because sample data lacks a precondition, report the failure and record it in that feature's Gotchas. Don't weaken the journey.

## Cleanup

```bash
$V/nopverify.sh stop       # stop the app; keep build, DB, install state for a fast relaunch
$V/nopverify.sh cleanup    # stop, DROP DATABASE nopverify_<name>, rm -rf /tmp/nop-verify/<name>
NOPVERIFY_NAME=installer $V/nopverify.sh cleanup
```

`stop` and `cleanup` kill only the pid recorded in `dotnet.pid` and the instance's own tmux session. Never kill by process name: the shared port-8080 `dotnet run` and its MSBuild nodes are on this machine too. Neither command touches the evidence folder or the Playwright install in `/tmp/nop-verify/.pylib`.

## Helpers

| File | Invocation |
| --- | --- |
| `scripts/nopverify.sh` | `nopverify.sh {launch,doctor,url,harness,py,psql,logs,stop,cleanup,help}`, configured by `NOPVERIFY_NAME`, `NOPVERIFY_PORT`, `NOPVERIFY_EVIDENCE` |
| `scripts/nopdrive.py` | `from nopdrive import Run, expect` inside scripts run by `nopverify.sh py` |
| `scripts/journeys/guest_checkout.py` | `nopverify.sh py $V/journeys/guest_checkout.py` |
| `scripts/journeys/install_wizard.py` | Needs a `--no-install` instance: `NOPVERIFY_NAME=installer NOPVERIFY_PORT=8091 nopverify.sh launch --no-install`, then `NOPVERIFY_NAME=installer nopverify.sh py $V/journeys/install_wizard.py` |

`nopverify.sh psql -c '<sql>'` opens psql on the instance database. PostgreSQL identifiers are quoted PascalCase: `"Order"."CustomOrderNumber"`, `"Customer"."Email"`.

## Feature map

`features/README.md` indexes the user-facing features, each with how to reach it, the handles to drive it, and the end state that proves it. Extend the map when you verify something it doesn't cover.

## Related

- `start-local-nopcommerce` and `stop-local-nopcommerce` are Docker workflows for a human's machine; Docker is not installed on this VM.
- Unit and integration tests: `dotnet test src/Tests/Nop.Tests`. They complement but don't replace a driven journey.
