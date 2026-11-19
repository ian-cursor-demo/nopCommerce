"""Fill the /install wizard in the browser and land on a storefront with sample data.

Needs an uninstalled instance: NOPVERIFY_NAME=installer NOPVERIFY_PORT=8091 nopverify.sh launch --no-install
Run:   NOPVERIFY_NAME=installer nopverify.sh py journeys/install_wizard.py
"""

import time

from playwright.sync_api import Error as PlaywrightError

from nopdrive import ADMIN_EMAIL, ADMIN_PASSWORD, DB, Run, expect

with Run("install-wizard") as run:
    page = run.page
    run.goto("/")
    run.check(page.url.rstrip("/").endswith("/install"), "uninstalled store redirects / to /install")

    page.fill("#AdminEmail", ADMIN_EMAIL)
    page.fill("#AdminPassword", ADMIN_PASSWORD)
    page.fill("#ConfirmPassword", ADMIN_PASSWORD)
    page.select_option("#Country", value="US-en-US")
    page.check("#InstallSampleData")
    page.uncheck("#SubscribeNewsletters")
    # Option values are 1/2/3 (PostgreSQL is 3). The label "PostgreSQL" matches too; "SqlServer" does not.
    page.select_option("#DataProvider", value="PostgreSQL")
    page.check("#CreateDatabaseIfNotExists")
    page.fill("#ServerName", "localhost")
    page.fill("#DatabaseName", DB)
    page.fill("#Username", "nopcommerce")
    page.fill("#Password", "nopCommerce_db_password")
    run.shot("install-form-filled", full_page=True)

    page.click("button.btn-install")
    # The wizard posts, calls /install/restartapplication, then redirects to the homepage. That redirect can
    # beat the restarted process, so the browser may land on a connection-refused page; reload like a user would.
    try:
        page.wait_for_url(lambda url: "/install" not in url, timeout=300_000)
    except PlaywrightError as e:
        if "ERR_CONNECTION_REFUSED" not in str(e):
            raise
        run.log("redirect after install hit the restarting app (connection refused); reloading until it is back")
    deadline = time.time() + 180
    while True:
        try:
            resp = page.goto("/")
            if resp and resp.status == 200:
                break
        except PlaywrightError:
            pass
        if time.time() > deadline:
            raise TimeoutError("storefront did not come back after install")
        time.sleep(2)
    expect(page.locator(".product-item .product-title a", has_text="Build your own computer")).to_be_visible(timeout=120_000)
    run.shot("storefront-after-install")

    products = int(run.db('select count(*) from "Product"')[0][0])
    run.check(products > 0, f"sample products installed ({products})")
    admins = run.db(
        'select c."Email" from "Customer" c join "Customer_CustomerRole_Mapping" m on m."Customer_Id" = c."Id" '
        'join "CustomerRole" r on r."Id" = m."CustomerRole_Id" '
        f"where r.\"SystemName\" = 'Administrators' and lower(c.\"Email\") = lower('{ADMIN_EMAIL}')"
    )
    run.check(len(admins) == 1, f"admin {ADMIN_EMAIL} exists in Administrators role")
    run.admin_login()
    run.shot("admin-dashboard")
