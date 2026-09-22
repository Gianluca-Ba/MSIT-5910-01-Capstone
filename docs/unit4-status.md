# Unit 4 implementation status

Prepared September 22, 2026. This is an implementation candidate for review, not a measured reliability evaluation.

## Implemented and locally checked

- .NET SDK 10.0.401, ASP.NET Core services, Microsoft.Data.SqlClient 7.1.0, xUnit 2.9.3. Exact dependency graphs are locked in the project lock files.
- ERP atomic order/outbox persistence and source-authorized status queries.
- WMS atomic order/receipt persistence, serializable key-range locking, unique source/order identities, original-receipt duplicates and immutable-content conflicts.
- Input validation, 4096-byte body limit, server-owned source mapping, separate API keys, and loopback-only HTTP endpoints.
- Single worker with a five-second HTTP timeout, persisted attempt outcome, and visible RecoveryRequired state for uncertain delivery. No database transaction spans HTTP.
- Release compilation succeeded with zero warnings/errors. All 28 unit tests passed with zero skipped tests. Unit tests cover validation boundaries, content comparison and acknowledgement classification.
- Both framework-dependent service packages were produced locally with XML settings included.

## Runtime verification completed

- `Initialize-Demo.ps1`: successfully created CapstoneErp and CapstoneWms plus their restricted logins after explicit host authorization. Existing same-named objects are protected by refusal checks.
- `Start-Demo.ps1`, `Stop-Demo.ps1`, `Invoke-Demo.ps1`: startup, stop, restart and the complete order/duplicate/conflict demonstration passed.
- `Test-Demo.ps1`: all 25 HTTP/SQL assertions passed, including ten concurrent receiver requests, one business effect, matching receipts, conflicts, authentication, source isolation and durable outgoing persistence.
- `Test-DeliveryFailure.ps1`: a real stopped WMS process produced RecoveryRequired with no receipt; the state persisted after restarting both services.
- Both application accounts were verified to lack sysadmin, database CONTROL, other-capstone-database access and order DELETE permissions. See `unit4-verification.md`.
- Hosted [workflow run 35679108009](https://github.com/Gianluca-Ba/MSIT-5910-01-Capstone/actions/runs/35679108009) completed successfully for implementation commit `87dc25d`. It restored locked dependencies, built, ran unit tests, and uploaded service packages plus test results. This validates hosted CI and packaging, not LAN database integration.

## Design decisions and next work

The Unit 3 target architecture originally described handling unique-key races by catching insert conflicts. This implementation takes serializable update/range locks before inserting while retaining the unique database key. The purpose is the same: serialize decisions for one identity and preserve exactly one committed order. The ten-request concurrent integration check verified this for the tested SQL Server workload.

Full rollback fault injection, durable retry schedules, authenticated manual recovery, lost-acknowledgement trials, baseline comparison and performance measurement remain later work. The unit test suite does not establish these properties.

Available student time is ten hours before the September 30 deadline. Review and recording take precedence over optional UI, machinery simulation or additional frameworks. The submitted video must show actual working behavior; the recording guide is a rehearsal aid rather than evidence of completed execution.
