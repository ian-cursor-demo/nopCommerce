# nopCommerce feature map

User-facing features of the app, each in its own file. Every file says how a user reaches the feature, how to drive it with `nopdrive` (Playwright), and which observable end state proves it works. All handles were checked against a sample-data install of this repo.

| Feature | Surface | Entry points | Scripted journey |
| --- | --- | --- | --- |
| [Install wizard](install-wizard.md) | `/install` | first visit to an uninstalled store | `journeys/install_wizard.py` (proven) |
| [Catalog browsing and search](catalog-and-search.md) | storefront | top menu categories, header search box, product pages | covered in part by `guest_checkout.py` (search → product) |
| [Cart and checkout](cart-and-checkout.md) | storefront | add-to-cart buttons, `/cart`, one-page checkout | `journeys/guest_checkout.py` (proven) |
| [Customer account](customer-account.md) | storefront | header Register / Log in / My account | handles proven by probe, no committed journey yet |
| [Admin: catalog, orders, customers](admin.md) | `/Admin` | log in as admin → Administration link or `/Admin` | handles proven by probe, no committed journey yet |

Sample data facts that journeys rely on:

- 47 products across 7 top categories: Computers, Electronics, Apparel, Digital downloads, Books, Jewelry, Gift Cards.
- Orders 1–5 are seeded. New orders continue from 6.
- "Fahrenheit 451 by Ray Bradbury" (`/fahrenheit-451-by-ray-bradbury`) costs $27.00, has no attributes, and ships by Ground for $0.00.
- "Build your own computer" requires attribute choices before add to cart.
- Active payment methods: Check / Money Order, Manual (credit card form), PayPal Commerce (needs live credentials; don't use it).
- Shipping is Fixed by weight / by total with methods Ground, Next Day Air, 2nd Day Air.

When a change touches a feature listed here, prove it through every entry point the file lists, not just the most convenient one.
