# AGENTS.md

## Dev loop

Run the tests for the area you changed. `dotnet test` does an incremental Debug build first, so you need no separate build step.

```bash
dotnet test src/Tests/Nop.Tests --filter "FullyQualifiedName~Nop.Tests.Nop.Services.Tests.Orders"
```

Test namespaces mirror the layer folders. Narrow the filter to the folder you touched:

- `Nop.Tests.Nop.Core.Tests`
- `Nop.Tests.Nop.Data.Tests`
- `Nop.Tests.Nop.Services.Tests` (for example `Nop.Tests.Nop.Services.Tests.Orders`)
- `Nop.Tests.Nop.Web.Tests`

`Nop.Tests` uses in-memory SQLite, so tests need no SQL Server and no Docker. It references only `Nop.Web`. Building it compiles Core, Data, Services, Web.Framework and Web, and skips the 30 plugin projects.

One service test takes about 15 s after an edit in `Nop.Services`. The 73 Orders tests take about 30 s. The full suite takes about 45 s, so narrow the filter while you iterate.

## Before you push

CI (`.github/workflows/dotnet.yml`) builds the whole solution in Release, plugins included, then runs every test. The local equivalent:

```bash
dotnet build src -c Release
dotnet test src -c Release --no-build
```

The pair takes about 80 s. The suite has 1,094 tests. Expect 8 skipped. The `ConcurrentTrieTests` profiling and concurrency cases carry `[Ignore]`.

## Gotchas

- The first test that derives from `BaseNopTest` costs about 5.4 s per test run. Its static constructor builds the DI container, creates the SQLite schema and installs sample data once per process. A run that touches only tests outside `BaseNopTest`, such as `SingletonTests`, finishes in about 1.4 s.
- The three `CanCheckVatNumber` cases in `Nop.Services.Tests/Tax/TaxServiceTests.cs` call the EU VIES and UK HMRC VAT web services. They fail without internet access.
- `CanExportOrdersXlsx` in `Nop.Services.Tests/ExportImport/ExportManagerTests.cs` is flaky. It failed in 4 of 13 runs, alone and in the full suite, with `Address.ShippingFirstName` empty. Rerun it once before you treat a failure as yours.
- Plugin `.csproj` files use `$(SolutionDir)` in `OutputPath` and in their project references. Build plugins through the solution with `dotnet build src/NopCommerce.sln`, never as a single project.
- Tests do not cover Razor views, JS or plugin wiring. Only the running app exercises them.

## Running the app

The running app needs a real database (SQL Server, MySQL or PostgreSQL) to get past `/install`. SQLite exists only in tests. To run it locally in Docker, follow `.cursor/skills/start-local-nopcommerce/SKILL.md`. Stop it with `.cursor/skills/stop-local-nopcommerce/SKILL.md`.

## Cursor Cloud specific instructions

Install the SDK from Ubuntu's archive. Egress rules block the Microsoft package feed and the dotnet-install CDN. `api.nuget.org` is reachable.

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0
```

This installs 10.0.113. `global.json` pins 10.0.100 with `rollForward: latestFeature`, and 10.0.113 satisfies it. A cold build of the test project, NuGet restore included, takes about 40 s.

The VM has no route to the VAT web services. Exclude those cases when you run the full suite:

```bash
dotnet test src/Tests/Nop.Tests --filter "FullyQualifiedName!~CanCheckVatNumber"
```

Expect 1,082 passed, 8 skipped, 0 failed, apart from the flaky `CanExportOrdersXlsx`.
