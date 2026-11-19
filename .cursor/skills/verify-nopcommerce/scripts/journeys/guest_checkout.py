"""Guest buys a book: search -> product page -> add to cart -> cart -> one-page checkout -> order placed.

Proves the order exists in the database with the right customer email, total and item.
Run: nopverify.sh py journeys/guest_checkout.py
"""

import time

from nopdrive import Run, expect

PRODUCT = "Fahrenheit 451 by Ray Bradbury"
EMAIL = f"guest-{int(time.time())}@example.com"

with Run("guest-checkout") as run:
    page = run.page
    orders_before = int(run.db('select count(*) from "Order"')[0][0])

    run.goto("/")
    page.fill("#small-searchterms", "Fahrenheit")
    page.press("#small-searchterms", "Enter")
    page.wait_for_load_state("domcontentloaded")
    run.check("/search" in page.url, "search box submits to /search")
    run.shot("search-results")

    page.locator(".product-item .product-title a", has_text=PRODUCT).click()
    expect(page.locator(".product-name h1")).to_have_text(PRODUCT)
    run.shot("product-page")

    page.locator("button[id^='add-to-cart-button-']").click()
    expect(page.locator("#bar-notification .content")).to_contain_text("shopping cart")
    run.wait_ajax()
    run.shot("added-to-cart-notification")

    run.goto("/cart")
    expect(page.locator(".cart .product-name", has_text=PRODUCT)).to_be_visible()
    page.check("#termsofservice")
    run.shot("cart")
    page.click("#checkout")

    page.click("button.checkout-as-guest-button")
    page.wait_for_url("**/onepagecheckout**")

    billing = page.locator("#checkout-step-billing")
    expect(billing).to_be_visible()
    page.fill("#BillingNewAddress_FirstName", "Verify")
    page.fill("#BillingNewAddress_LastName", "Guest")
    page.fill("#BillingNewAddress_Email", EMAIL)
    page.select_option("#BillingNewAddress_CountryId", label="United States of America")
    run.wait_ajax()
    page.select_option("#BillingNewAddress_StateProvinceId", label="New York")
    page.fill("#BillingNewAddress_City", "New York")
    page.fill("#BillingNewAddress_Address1", "1 Verification Way")
    page.fill("#BillingNewAddress_ZipPostalCode", "10001")
    page.fill("#BillingNewAddress_PhoneNumber", "5555550100")
    run.shot("billing-address")
    billing.locator("button.new-address-next-step-button").click()
    run.wait_ajax()

    shipping_method = page.locator("#checkout-step-shipping-method")
    expect(shipping_method).to_be_visible()
    shipping_method.locator("input[name='shippingoption']").first.check()
    run.shot("shipping-method")
    shipping_method.locator("button.shipping-method-next-step-button").click()
    run.wait_ajax()

    payment_method = page.locator("#checkout-step-payment-method")
    expect(payment_method).to_be_visible()
    payment_method.locator("input[name='paymentmethod'][value='Payments.CheckMoneyOrder']").check()
    run.shot("payment-method")
    payment_method.locator("button.payment-method-next-step-button").click()
    run.wait_ajax()

    payment_info = page.locator("#checkout-step-payment-info")
    expect(payment_info).to_be_visible()
    payment_info.locator("button.payment-info-next-step-button").click()
    run.wait_ajax()

    confirm = page.locator("#checkout-step-confirm-order")
    expect(confirm).to_be_visible()
    expect(confirm.locator(".product-name", has_text=PRODUCT)).to_be_visible()
    shown_total = confirm.locator("tr.order-total .value-summary strong").inner_text()
    run.log(f"confirm step shows order total {shown_total!r}")
    run.shot("confirm-order", full_page=True)
    confirm.locator("button.confirm-order-next-step-button").click()

    page.wait_for_url("**/checkout/completed**")
    order_number_text = page.locator(".order-number strong").inner_text()
    run.shot("order-completed")
    order_number = order_number_text.split(":")[-1].strip()
    run.check(order_number.isdigit(), f"completed page shows an order number ({order_number_text!r})")

    rows = run.db(
        'select o."Id", o."CustomOrderNumber", a."Email", o."OrderTotal", o."PaymentMethodSystemName", '
        'o."OrderStatusId", oi."Quantity", p."Name" '
        'from "Order" o join "Address" a on a."Id" = o."BillingAddressId" '
        'join "OrderItem" oi on oi."OrderId" = o."Id" join "Product" p on p."Id" = oi."ProductId" '
        f"where o.\"CustomOrderNumber\" = '{order_number}'"
    )
    run.check(len(rows) == 1, "exactly one order row with one item for that order number")
    _, _, email, total, payment, status, qty, name = rows[0]
    run.check(email == EMAIL, f"order billing email is {EMAIL}")
    run.check(name == PRODUCT and qty == "1", f"order item is 1 x {PRODUCT}")
    run.check(payment == "Payments.CheckMoneyOrder", "order paid with Check / Money Order")
    run.check(status == "10", "order status is Pending (10)")
    shown = float(shown_total.replace("$", "").replace(",", ""))
    run.check(abs(float(total) - shown) < 0.005, f"stored order total {total} equals total shown at confirm ({shown_total})")
    run.check(float(total) >= 27.0, f"order total {total} covers the 27.00 item price")
    orders_after = int(run.db('select count(*) from "Order"')[0][0])
    run.check(orders_after == orders_before + 1, f"order count went {orders_before} -> {orders_after}")
