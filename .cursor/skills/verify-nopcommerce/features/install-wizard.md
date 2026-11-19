# Install wizard

The first-run page where a store owner picks a database, creates the admin account, and optionally loads sample data. Code: `Controllers/InstallController.cs`, `Views/Install/Index.cshtml`, `Views/Install/_Install.ConnectionString.cshtml`.

## Sub-features

- Redirect from any URL to `/install` while the store is uninstalled (`InstallUrlMiddleware`).
- Language switcher for the wizard itself (`#language`, which navigates to `/installchangelanguage/<code>`).
- Admin email, password, and confirm password.
- Country / regional resources (`#Country`). A non-en-US culture downloads a language pack from nopcommerce.com.
- Sample data checkbox and newsletter checkbox. The newsletter calls nopcommerce.com.
- Data provider (SQL Server, MySQL, PostgreSQL), create database if missing, and raw connection string mode.
- Restart after install, which installs plugins on the next start.

## How to get to it (user POV)

Open any URL on a fresh store. You land on `/install`, fill the form, and click **Install**. A "restarting" message shows, then the browser goes to the homepage of the new store.

## Driving it with nopdrive

Needs an uninstalled instance:

```bash
NOPVERIFY_NAME=installer NOPVERIFY_PORT=8091 $V/nopverify.sh launch --no-install
NOPVERIFY_NAME=installer $V/nopverify.sh py $V/journeys/install_wizard.py
```

Handles:

- Admin account: `#AdminEmail`, `#AdminPassword`, `#ConfirmPassword`.
- Region and options: `#Country` (value `US-en-US`), `#InstallSampleData`, `#SubscribeNewsletters`.
- Database: `#DataProvider` (values `SqlServer`, `MySql`, `PostgreSQL`), `#CreateDatabaseIfNotExists`, `#ServerName`, `#DatabaseName`, `#Username`, `#Password`, plus `#ConnectionStringRaw` / `#ConnectionString` for raw mode.
- Submit: `button.btn-install`.
- Errors: `.validation-summary-errors`.

Proof of success:

- The browser leaves `/install` and the homepage lists "Build your own computer".
- `"Product"` has rows.
- The admin email sits in the `Administrators` role.
- `run.admin_login()` reaches `/Admin`.
- `app/App_Data/appsettings.json` now has a non-empty `ConnectionStrings.ConnectionString`.

Every `nopverify.sh launch` without `--no-install` also exercises this feature: it POSTs the same form with curl. Check `logs/install-post.html` when that step fails.

## Gotchas

- Use the instance's own DB name (`nopverify_<name>`, exposed as `nopdrive.DB`). Then `cleanup` drops it.
- Keep `SubscribeNewsletters` unchecked and Country `US-en-US`. Other values make outbound calls to nopcommerce.com, which can hang or fail on a restricted network.
- After a successful POST, the app **stops its own process** (`RestartAppDomain`). The instance's restart loop brings it back, and requests in that window get connection refused rather than 5xx.
- The wizard's JavaScript redirects to `/` as soon as the restart call returns, which is usually before the new process listens. Chrome then shows `ERR_CONNECTION_REFUSED`, and a real user sees the same thing until they reload. `install_wizard.py` logs this and reloads `/` until it returns 200 (about 15 s). Don't count that refused navigation as a failure; do count a store that never comes back.
- A failed install resets the connection string. You can submit again without relaunching.
- Install state is in `App_Data/appsettings.json`, not `dataSettings.json`. To re-test the wizard, use `nopverify.sh launch --fresh --no-install`.
