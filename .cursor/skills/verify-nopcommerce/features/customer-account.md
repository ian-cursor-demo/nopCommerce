# Customer account

Registration, login and logout, and the "My account" pages. Code: `Controllers/CustomerController.cs`, `Controllers/OrderController.cs`, `Views/Customer/*`, `Views/Order/*`.

## Sub-features

- Register at `/register`: gender, first and last name, email, password, newsletter. The result page is `/registerresult/1`.
- Log in at `/login` and log out with the `.ico-logout` header link.
- My account at `/customer/info`, with:
  - addresses (`/customer/addresses`);
  - orders (`/order/history`);
  - downloadable products, back-in-stock subscriptions, reward points;
  - change password (`/customer/changepassword`);
  - avatar.
- Password recovery at `/passwordrecovery`. It sends email through the queued email table; no SMTP is configured, so check `"QueuedEmail"`.
- Wishlist (`/wishlist`) and the header links `.ico-register`, `.ico-login`, `.ico-account`, `.ico-wishlist`, `.ico-cart`.

## How to get to it (user POV)

Click **Register** in the header, fill in the form and submit; you are logged in. Click **My account** to see and edit your profile, addresses and orders. **Log out** and **Log in** are in the header.

## Driving it with nopdrive

```python
email = f"user-{int(time.time())}@example.com"
run.goto("/register")
page.check("#gender-female")
page.fill("#FirstName", "Probe"); page.fill("#LastName", "User"); page.fill("#Email", email)
page.fill("#Password", "Probe123!"); page.fill("#ConfirmPassword", "Probe123!")
page.click("#register-button")
page.wait_for_url("**/registerresult/**")       # .result -> "Your registration completed"
run.check(page.locator(".ico-logout").count() > 0, "registered customer is logged in")
run.goto("/customer/info")
run.check(page.input_value("#Email") == email, "my account shows the email")
page.click(".ico-logout")
run.login(email, "Probe123!")                   # nopdrive helper: #Email, #Password, button.login-button
```

Proof of success:

- UI: the result page text and the `.ico-logout` link are present; `/customer/info` shows the new values.
- Database: `select "Email","Active" from "Customer" where "Email" = '<email>'` returns one active row. The customer is in the `Registered` role (`"Customer_CustomerRole_Mapping"` joined to `"CustomerRole"`).
- Admin: `/Admin/Customer/List` with `#SearchEmail` set to that email shows the row (see [admin.md](admin.md)).

All of these handles were exercised against a sample-data install. There is no committed journey yet.

## Gotchas

- Registration type is `Standard` (`customersettings.userregistrationtype`), so the customer is active right away. With email validation or admin approval switched on, the journey must check the `"QueuedEmail"` row, or approve the customer in admin, before logging in.
- Use a unique email per run. Registering an email that already exists fails validation on the register page.
- The password policy defaults to a minimum length of 6.
- Logging in merges the guest cart into the customer's cart. Clear the cart, or use a fresh context, when a cart count matters.
