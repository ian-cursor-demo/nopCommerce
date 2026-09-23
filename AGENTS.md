# AGENTS.md

Instructions for coding agents working in this nopCommerce fork.

nopCommerce is an open-source ASP.NET Core e-commerce platform. This fork targets .NET 10 (`global.json` pins SDK `10.0.100` with `latestFeature` roll-forward). The solution is `src/NopCommerce.sln` and the web entry point is `src/Presentation/Nop.Web/Program.cs`. The default branch is `develop`.

## Layers

| Layer | Project | Role |
|-------|---------|------|
| Core | `src/Libraries/Nop.Core` | Domain entities, settings, caching, events, `NopEngine` |
| Data | `src/Libraries/Nop.Data` | `IRepository<T>`, LinqToDB (not EF Core), FluentMigrator migrations |
| Services | `src/Libraries/Nop.Services` | Business logic (catalog, orders, customers, payments, ...) |
| Framework | `src/Presentation/Nop.Web.Framework` | MVC infrastructure, DI registration, middleware |
| Web | `src/Presentation/Nop.Web` | Public controllers, admin area (`Areas/Admin`), model factories, views, themes |
| Plugins | `src/Plugins/Nop.Plugin.*` | Payments, shipping, tax, widgets, external auth, misc; one project per plugin |
| Tests | `src/Tests/Nop.Tests` | NUnit tests for every layer |

**Dependency direction:** Web → Framework → Services → Data → Core. A project may only depend on layers to its right; never add a reference from a lower layer to a higher one (for example, Services must not use anything from Nop.Web.Framework). Plugins reference `Nop.Web` and reach Services and Core through it. Core has no project references.

A typical request goes: middleware (sets `IWorkContext`: customer, language, currency, store) → thin controller → model factory (`Nop.Web/Factories`, entity to view model) → service (business rules) → `IRepository<T>` → database. Put business rules in services, not controllers or factories.

Conventions worth knowing before you change anything:

- Entities live in `Nop.Core/Domain/{Area}/` and inherit `BaseEntity`. Settings classes implement `ISettings` and are stored in the database.
- Cross-cutting reactions use events: publish through `IEventPublisher`, handle with `IConsumer<TEvent>` in Services.
- Plugins are loaded from `Nop.Web/Plugins/{SystemName}/` at startup and resolved through `PluginDescriptor.Instance<T>()`, not registered in DI by interface. Each plugin assembly has exactly one `IPlugin` implementation, and its `plugin.json` `SupportedVersions` must match `NopVersion.CURRENT_VERSION` or the plugin is skipped.
- Public storefront controllers derive from `BasePublicController`; admin controllers derive from `BaseAdminController`.
- Follow `.editorconfig`: C# files use 4-space indentation and UTF-8 with BOM. Keep file-scoped namespaces and the existing `#region` layout in the files you edit.

## Build and test

Run this from the repo root and make sure it passes before you push. It mirrors the `.NET` GitHub Actions workflow, which runs the same build and tests on `windows-latest` for every PR to `develop`.

```bash
dotnet build src -c Release && dotnet test src -c Release --no-build
```

Tests are NUnit under `src/Tests/Nop.Tests`, organised by layer (`Nop.Core.Tests`, `Nop.Data.Tests`, `Nop.Services.Tests`, `Nop.Web.Tests`) and then by area, with namespaces that match the folders (for example `Nop.Tests.Nop.Services.Tests.Catalog`). Database-backed tests use an in-memory SQLite provider; service tests derive from `ServiceTest`, which derives from `BaseNopTest`. Assertions use AwesomeAssertions (`.Should()`) and mocks use Moq.

When you change behaviour, add or update tests next to the existing ones for that area. To iterate on one area, filter the test run, then run the full command above before pushing:

```bash
dotnet test src -c Release --no-build --filter "FullyQualifiedName~Nop.Tests.Nop.Services.Tests.Catalog"
```

To run the site locally, use the Docker workflow in `.cursor/skills/start-local-nopcommerce/SKILL.md`. A running site is not required to build or test.

## Rules

- Do not add NuGet packages or change package versions. Solve the problem with what the solution already references.
- Do not edit `.csproj` files, `src/Directory.Build.props`, `global.json`, anything under `.github/`, or anything under `upgradescripts/`. If a change seems to need one of these, stop and say so instead of making it.
- Keep PRs small: one logical change per PR, ideally under 200 changed lines. Do not reformat, rename, or reorder code you are not otherwise changing.
- Do not push with a failing build or failing tests.
