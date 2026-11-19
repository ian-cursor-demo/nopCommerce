# Cart and checkout

Putting products in the cart and placing an order. Code: `Controllers/ShoppingCartController.cs`, `Controllers/CheckoutController.cs`, `Views/ShoppingCart/*`, `Views/Checkout/*` (one-page checkout is the `Opc*` views), `Views/Shared/Components/OrderSummary` and `OrderTotals`.

## Sub-features

- Add to cart from the product page (`button[id^='add-to-cart-button-']`), with a success bar (`#bar-notification`).
- Cart page `/cart`: update quantity, remove, discount and gift card codes, estimate shipping, terms of service (`#termsofservice`), Checkout button (`#checkout`).
- Checkout entry for guests: the login page shows **Checkout as Guest** (`button.checkout-as-guest-button`) and Register.
- One-page checkout `/onepagecheckout` (on by default). It has six steps:
  1. Billing address
  2. Shipping address (skipped while "ship to same address" is checked)
  3. Shipping method
  4. Payment method
  5. Payment info
  6. Confirm
- Multi-page checkout (`/checkout/billingaddress` and so on) when the `ordersettings.onepagecheckoutenabled` setting is off.
- Order completed page `/checkout/completed` with the order number.
- Registered checkout: saved addresses (`.select-billing-address-button`) and order history afterwards.

## How to get to it (user POV)

1. On a product page, click **Add to cart**, then the **Shopping cart** header link.
2. Tick the terms checkbox and click **Checkout**.
3. As a guest, choose **Checkout as Guest**.
4. Fill in the billing address and continue through shipping method, payment method and payment info.
5. Click **Confirm**.

## Driving it with nopdrive

The whole path is scripted and proven:

```bash
$V/nopverify.sh py $V/journeys/guest_checkout.py
```

Step containers and buttons, in order:

| Step | Container | Inputs | Button |
| --- | --- | --- | --- |
| Billing | `#checkout-step-billing` | `#BillingNewAddress_{FirstName,LastName,Email,CountryId,StateProvinceId,City,Address1,ZipPostalCode,PhoneNumber}` | `.new-address-next-step-button` |
| Shipping method | `#checkout-step-shipping-method` | `input[name='shippingoption']` | `.shipping-method-next-step-button` |
| Payment method | `#checkout-step-payment-method` | `input[name='paymentmethod'][value='Payments.CheckMoneyOrder']` | `.payment-method-next-step-button` |
| Payment info | `#checkout-step-payment-info` | none for Check / Money Order | `.payment-info-next-step-button` |
| Confirm | `#checkout-step-confirm-order` | `tr.order-total .value-summary strong` shows the total | `.confirm-order-next-step-button` |

Scope each button to its step container. Every step has a "continue" button in the DOM.

Proof of success: the journey checks all of these:

- The completed page shows `.order-number`.
- Exactly one `"Order"` row exists for that `CustomOrderNumber`, joined to `"Address"` (billing email), `"OrderItem"` and `"Product"`.
- The payment method is `Payments.CheckMoneyOrder` and `OrderStatusId = 10` (Pending).
- The stored `OrderTotal` equals the total shown at confirm.
- The order count went up by exactly 1.

For cross-surface proof, the new order also appears first in admin `/Admin/Order/List` (see [admin.md](admin.md)).

## Gotchas

- Every step posts over jQuery AJAX. Call `run.wait_ajax()` after each continue click and before checking the next step.
- Choosing a country loads its states over AJAX. Wait before `select_option` on `#BillingNewAddress_StateProvinceId`. The US option label is "United States of America".
- The sample store charges $0.00 Ground shipping on this order, so the total equals the item price. Assert against the total shown at confirm, not against a guessed shipping fee.
- `inner_text()` returns CSS-uppercased text ("ORDER NUMBER: 9").
- The terms of service checkbox is on the **cart** page (`ordersettings.termsofserviceonshoppingcartpage = True`), not on confirm.
- Avoid PayPal Commerce: it needs live credentials. "Manual" payment requires card form fields in the payment info step.
- "Build your own computer" and the apparel items need attribute choices before add to cart works.
