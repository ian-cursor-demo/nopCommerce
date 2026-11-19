# Admin: catalog, orders, customers

The back office in `/Admin`, an AdminLTE layout with DataTables grids. Code: `Areas/Admin/Controllers/*`, `Areas/Admin/Views/*`, `Areas/Admin/Factories/*`.

## Sub-features

- Dashboard `/Admin`: order and customer statistics, latest orders, popular search terms.
- Catalog:
  - products list `/Admin/Product/List` (search `#SearchProductName`, button `#search-products`, grid `#products-grid`);
  - create `/Admin/Product/Create` and edit `/Admin/Product/Edit/{id}` (`button[name='save']`, `button[name='save-continue']`);
  - categories, manufacturers, attributes.
- Sales:
  - orders list `/Admin/Order/List` (grid `#orders-grid`, search `#search-orders`);
  - order details `/Admin/Order/Edit/{id}`: status changes, shipments, notes;
  - shipments and returns.
- Customers: list `/Admin/Customer/List` (`#SearchEmail`, `#search-customers`, grid `#customers-grid`), edit, roles, impersonation.
- Configuration:
  - settings pages;
  - plugins `/Admin/Plugin/List` (install or uninstall restarts the app);
  - payment and shipping methods;
  - local plugins.
- System: log `/Admin/Log/List`, schedule tasks, maintenance.

## How to get to it (user POV)

Log in as `admin@yourstore.com` / `Admin123!` through the storefront **Log in** link. The header then shows an **Administration** link; or go to `/Admin`. The left sidebar groups Catalog, Sales, Customers, Promotions, Content management, Configuration and System.

## Driving it with nopdrive

```python
run.admin_login()                                          # storefront login, then /Admin; h1 "Dashboard"
run.goto("/Admin/Product/List")
page.fill("#SearchProductName", "Fahrenheit"); page.click("#search-products"); run.wait_ajax()
expect(page.locator("#products-grid tbody tr", has_text="Fahrenheit 451")).to_be_visible()

run.goto("/Admin/Order/List"); run.wait_ajax()
first_row = page.locator("#orders-grid tbody tr").first.inner_text()   # newest order first
run.goto(f"/Admin/Order/Edit/{order_id}")                  # h1 "Edit order details - <id>"

run.goto("/Admin/Customer/List")
page.fill("#SearchEmail", email); page.click("#search-customers"); run.wait_ajax()
expect(page.locator("#customers-grid tbody tr", has_text=email)).to_be_visible()
```

Proof of success:

- A grid row with the expected values shows on screen.
- The edit page reflects saved values after a reload.
- The matching DB row changed: `"Product"."Price"`, `"Order"."OrderStatusId"`, `"Setting"."Value"` and so on.
- For a storefront-facing admin change, such as a price edit or unpublishing a product, finish on the storefront page that shows the effect.

All handles here were exercised against a sample-data install. After `guest_checkout.py`, the first row of `#orders-grid` showed the new guest order with its email and total. There is no committed journey yet.

## Gotchas

- Admin grids load over AJAX after the page loads. Call `run.wait_ajax()` before reading rows, and again after clicking a search button.
- Admin edit pages hide fields marked `.advanced-setting` until the **Basic / Advanced** switch (`#advanced-settings-mode`, top right of the page) is on. Pages are also split into collapsible `nop-card` sections. Turn on advanced mode, and open the card, before filling a hidden field.
- Installing or uninstalling a plugin from `/Admin/Plugin/List`, and the header's `#restart-application` button, restart the app process. The restart loop brings it back within seconds; wait for `GET /` to return 200 before going on.
- The admin email is stored lowercased (`admin@yourstore.com`).
- The Admin area uses the same auth cookie as the storefront. Use a separate `Run` (a fresh browser context) to keep a shopper session and an admin session apart.
