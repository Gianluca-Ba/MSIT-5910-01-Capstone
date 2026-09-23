# Reliable ERP-WMS Integration

Master of Information Technology capstone exploring durable order delivery, idempotent warehouse acceptance, and controlled recovery in C# and SQL.

## Current status

Unit 4 initial implementation: two ASP.NET Core services, SQL transaction boundaries, API authentication, a bounded outbox worker, duplicate-safe acceptance, unit tests, demo scripts, and a build/test/package workflow. The Release build, 28 unit tests, and 25 real HTTP/SQL integration assertions pass. The end-to-end demo, WMS outage handling, restart persistence, and restricted database permissions have been verified. See [verification evidence](docs/unit4-verification.md) for the tested scope and remaining limits.

Start with [setup and deployment](docs/deployment.md), then the [API walkthrough](demo/walkthrough.md). Open `ReliableErpWms.slnx` in Visual Studio or use the .NET CLI with the SDK pinned in `global.json`.

## Structure

- `src/`: ERP submission service, WMS acceptance service, shared contracts and SQL logic.
- `tests/`: xUnit validation and delivery-outcome tests.
- `scripts/`: database initialization, startup, shutdown, demo and HTTP/SQL assertions.
- `database/`: separate schemas and read-only inspection queries.
- `docs/`: coursework preparation and engineering specifications.
- `design/`: architecture and sequence diagram sources.

The scope is order creation and acceptance, with a local verification dashboard for input, delivery status, duplicate/conflict checks, and SQL-backed warehouse evidence. It excludes production SAP access, machinery simulation, robot dispatch, PLC controls, and a full administration interface. Only original synthetic test records will be used.

With the demo services running, run `./scripts/Open-Dashboard.ps1`, paste the copied ERP API key, and click **Connect**. See [dashboard walkthrough](docs/dashboard.md). The page is served by ERP at `http://127.0.0.1:5080/`; no separate frontend package installation is needed.

The [automatic message lab](docs/automatic-sender.md) adds 1,000 SQL-backed templates, configurable seeded batches, Arm/Run/Stop controls, a live result map, and durable payload/timestamp/outcome records. Existing databases need the additive `scripts/Update-AutomationDatabase.ps1` migration before using it.

The [message designer](docs/message-customization.md) adds multiple versioned message types, named sections, field rules, seeded data generation and SQL-persisted validation evidence. This first milestone validates custom messages in ERP; it does not dispatch them to WMS. Existing databases need `scripts/Update-CustomizationDatabase.ps1`.

Unit 4 makes one bounded delivery attempt per pending record. Uncertain outcomes become `RecoveryRequired`; scheduled retries and audited recovery are Unit 5 work. Both HTTP APIs are loopback-only; SQL credentials and API keys stay in private local configuration.
