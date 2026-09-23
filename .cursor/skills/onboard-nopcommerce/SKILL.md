---
name: onboard-nopcommerce
description: Onboard to this nopCommerce codebase — architecture, layers, request flow, and plugin system. Use when starting work in this repo, explaining the codebase to a new teammate, or tracing how plugins integrate with core.
---

# Onboard to nopCommerce

## What this repo is

Open-source ASP.NET Core e-commerce platform (fork targets **.NET 10**). Solution: `src/NopCommerce.sln`. Entry point: `src/Presentation/Nop.Web/Program.cs`.

| Layer | Project | Role |
|-------|---------|------|
| Core | `Libraries/Nop.Core` | Domain entities, settings, caching, events, `NopEngine` |
| Data | `Libraries/Nop.Data` | `IRepository<T>`, LinqToDB (not EF), FluentMigrator |
| Services | `Libraries/Nop.Services` | Business logic (Catalog, Orders, Customers, Payments, …) |
| Framework | `Presentation/Nop.Web.Framework` | MVC infra, DI registration, middleware |
| Web | `Presentation/Nop.Web` | Controllers, views, themes, admin area |
| Plugins | `src/Plugins/` (31) | Swappable features — payments, shipping, tax, widgets, auth |
| Tests | `src/Tests/Nop.Tests/` | NUnit; SQLite in-memory for DB tests |

**Dependency direction:** Web → Framework → Services → Data → Core. Plugins reference Web and Services.

## Repo layout

```
src/
├── Libraries/     Nop.Core, Nop.Data, Nop.Services
├── Presentation/  Nop.Web, Nop.Web.Framework
├── Plugins/       Nop.Plugin.* (separate csproj per plugin)
└── Tests/
Dockerfile         # .NET 10 Alpine; publishes Nop.Web
.cursor/skills/    start-local-nopcommerce, stop-local-nopcommerce, this skill
```

## Startup flow

1. `Program.cs` loads `appsettings.json`, calls `ConfigureApplicationSettings` + `ConfigureApplicationServices`
2. `InitializePlugins()` runs **before** full DI — scans `~/Plugins/`, loads installed plugin DLLs as ASP.NET Application Parts
3. `NopEngine` discovers all `INopStartup` implementations, runs `IStartupTask`, registers AutoMapper
4. After DB is installed, `AppStartedConsumer` handles `AppStartedEvent`: install/update plugins, migrations, scheduler

## Request flow (typical feature)

```
HTTP → Middleware (WorkContext: customer, language, store)
     → Controller (thin)
     → Model Factory (entity → view model)
     → Service (business rules)
     → IRepository<T> (cached LinqToDB queries)
     → Database
```

**Public storefront:** `Nop.Web/Controllers/` (~23 controllers), base `BasePublicController`  
**Admin:** `Nop.Web/Areas/Admin/Controllers/` (~60 controllers), base `BaseAdminController`, URL prefix `/Admin`

## Key patterns

| Pattern | Where | Notes |
|---------|-------|-------|
| Entities | `Nop.Core/Domain/{Area}/` | Inherit `BaseEntity`; many use `ISoftDeletedEntity`, `IAclSupported` |
| Settings | `*Settings : ISettings` in Domain | DB-backed config, injected into services |
| WorkContext | `IWorkContext` | Per-request customer, language, currency, store |
| Events | `IConsumer<T>` in Services | Pub/sub decoupling (e.g. `OrderPaidEvent`) |
| Factories | `Nop.Web/Factories/` | Map domain → Razor view models |

## Plugin system (core extension model)

Plugins are **not optional glue** — payments, shipping, tax, widgets, and external auth are all plugins.

### Anatomy (example: `Nop.Plugin.Payments.Manual`)

```
Plugins/Payments.Manual/
├── plugin.json              # metadata; SystemName = "Payments.Manual"
├── Nop.Plugin.Payments.Manual.dll
├── ManualPaymentProcessor.cs   # implements IPaymentMethod + BasePlugin
├── Controllers/             # admin config (discovered via Application Part)
├── Components/              # ViewComponents for storefront UI
└── Views/                   # ~/Plugins/Payments.Manual/Views/...
```

Build output goes directly to `Presentation/Nop.Web/Plugins/{SystemName}/` via csproj `OutputPath`.

### Lifecycle

1. **Build** — plugin DLL + `plugin.json` + views copy to `Nop.Web/Plugins/`
2. **Startup** — `PluginsInfo.LoadPluginInfo()` scans `plugin.json`; only **installed** or **pending-install** plugins get assemblies loaded
3. **Install** — admin queues install → writes `App_Data/plugins.json` → **app restart** → `InstallPluginsAsync()` runs migrations + `InstallAsync()`
4. **Activate** — separate step per type (e.g. add system name to `PaymentSettings.ActivePaymentMethodSystemNames`)
5. **Runtime** — `PluginManager<T>` → `PluginService` → `PluginDescriptor.Instance<T>()` → `ResolveUnregistered()` (constructor DI from main container)

**State files:** `App_Data/plugins.json` (installed, pending install/uninstall), `App_Data/installedPlugins.json` (legacy).

### Plugin interfaces & managers

| Interface | Manager | Examples |
|-----------|---------|----------|
| `IPaymentMethod` | `PaymentPluginManager` | Payments.Manual, PayPal |
| `IShippingRateComputationMethod` | `ShippingPluginManager` | Shipping.UPS |
| `ITaxProvider` | `TaxPluginManager` | Tax.Avalara |
| `IWidgetPlugin` | `WidgetPluginManager` | Widgets.GoogleAnalytics |
| `IExternalAuthenticationMethod` | `AuthenticationPluginManager` | ExternalAuth.Facebook |

All extend `IPlugin` (`InstallAsync`, `UninstallAsync`, `GetConfigurationPageUrl`).

### Checkout trace (Payments.Manual)

```
CheckoutController.PaymentMethod
  → PaymentPluginManager.LoadActivePluginsAsync (filters by PaymentSettings.Active*)
  → ManualPaymentProcessor.GetPaymentMethodDescriptionAsync()

CheckoutController.PaymentInfo
  → LoadPluginBySystemNameAsync("Payments.Manual")
  → GetPublicViewComponent() → PaymentManualViewComponent → PaymentInfo.cshtml
  → ValidatePaymentFormAsync / GetPaymentInfoAsync

OrderProcessingService.PlaceOrderAsync
  → PaymentService.ProcessPaymentAsync
  → LoadPluginBySystemNameAsync → ManualPaymentProcessor.ProcessPaymentAsync
  → sets PaymentStatus from ManualPaymentSettings.TransactMode
```

### Plugin rules for implementers

- **One `IPlugin` class per assembly** — loader picks the single concrete implementation
- Plugins are **not** registered in DI as their interface; use `PluginDescriptor.Instance<T>()`
- Plugin constructor params **are** resolved from main DI (`ResolveUnregistered`)
- Views use explicit paths: `~/Plugins/{SystemName}/Views/...`
- `SupportedVersions` in `plugin.json` must match `NopVersion.CURRENT_VERSION` or plugin is skipped

## Important checkout services

`IShoppingCartService` → `IOrderProcessingService.PlaceOrderAsync()` → `IPaymentService` / `IShippingService` → plugin managers.

## Local dev

Prefer Docker (repo targets .NET 10; host may lack SDK). On Apple Silicon use `--platform linux/amd64`.

```bash
docker build --platform linux/amd64 -t nopcommerce-local-amd64 .
docker run --platform linux/amd64 --name nopcommerce-local -p 8080:80 nopcommerce-local-amd64
# First visit: http://localhost:8080/install
```

See `start-local-nopcommerce` and `stop-local-nopcommerce` skills for full workflow.

## Where to start reading code

| Goal | Start here |
|------|------------|
| App boot | `Program.cs`, `NopEngine.cs`, `ApplicationPartManagerExtensions.cs` |
| DI / services | `Nop.Web.Framework/Infrastructure/NopStartup.cs` |
| Typical service | `Nop.Services/Catalog/ProductService.cs` |
| Typical controller | `Nop.Web/Controllers/CatalogController.cs` |
| Plugin loading | `PluginsInfo.cs`, `PluginService.cs`, `PluginManager.cs` |
| Example plugin | `Plugins/Nop.Plugin.Payments.Manual/ManualPaymentProcessor.cs` |
| Plugin install on start | `AppStartedConsumer.cs` |

## Common pitfalls

- Plugin on disk ≠ loaded — must be **installed** (and app restarted)
- Plugin installed ≠ active — payment/shipping/tax also need **activation** in admin settings
- Uninstalled plugin assemblies are **not** loaded into the app domain
- Don't use default `docker-compose.yml` on Apple Silicon for DB (SQL Server is x64-only); app-only container is enough for installer
