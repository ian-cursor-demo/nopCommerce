# Catalog browsing and search

How shoppers find products: the homepage, category and manufacturer pages, product detail pages, and search. Code: `Controllers/CatalogController.cs`, `Controllers/ProductController.cs`, `Views/Catalog/*`, `Views/Product/*`, `Themes/DefaultClean`.

## Sub-features

- Homepage featured products (`.product-item` tiles).
- Top menu categories, e.g. `/books`, `/computers`, `/apparel`.
- Category page sorting (`#products-orderby`), page size, grid/list view mode. These are query params `orderby`, `pagesize`, `viewmode`.
- Header search box with autocomplete (`#small-searchterms`) and the search results page (`/search?q=`) with advanced search. With the install default `catalogsettings.showsearchtermhistory` on, focusing the empty box lists previous terms in `.ui-autocomplete`.
- Product detail page: name (`.product-name h1`), price, attributes, quantity, add to cart / wishlist / compare.
- Recently viewed, new products, compare list (footer links).

## How to get to it (user POV)

On the homepage, click a top menu category, or type in **Search store** in the header and press Enter. Click a product tile title to open its page.

## Driving it with nopdrive

```python
run.goto("/")
page.fill("#small-searchterms", "Fahrenheit"); page.press("#small-searchterms", "Enter")
page.locator(".product-item .product-title a", has_text="Fahrenheit 451 by Ray Bradbury").click()
expect(page.locator(".product-name h1")).to_have_text("Fahrenheit 451 by Ray Bradbury")

run.goto("/books")
page.select_option("#products-orderby", label="Price: Low to High"); run.wait_ajax()
first = page.locator(".product-item .product-title a").first.inner_text()   # "Pride and Prejudice" in sample data
```

Proof of success:

- The URL changes: `/search?q=...`, `/books?...orderby=10`.
- The expected product tiles show and their order matches the sort.
- The product page `h1` matches.
- For data-driven changes, compare with `"Product"` / `"Product_Category_Mapping"` rows through `run.db`.

`journeys/guest_checkout.py` covers search → product page as its first steps.

## Gotchas

- Category sorting and paging reload the product list over AJAX and rewrite the query string. Call `run.wait_ajax()` before reading tiles.
- Product URLs are slugs from `"UrlRecord"`, e.g. `/fahrenheit-451-by-ray-bradbury`. Look them up with `nopverify.sh psql` rather than guessing.
- Sort option labels are localized text. When the language changes, select by value (`10` is price low to high) instead.
- Search uses the built-in provider unless the `Search.Lucene` plugin is configured. Results ignore products that aren't published or aren't visible individually.
