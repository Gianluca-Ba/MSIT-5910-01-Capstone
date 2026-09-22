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

## Prepared but awaiting runtime verification

- `Initialize-Demo.ps1`: creates only CapstoneErp and CapstoneWms plus their restricted logins after explicit host authorization. It refuses to alter pre-existing same-named databases or logins.
- `Start-Demo.ps1`, `Stop-Demo.ps1`, `Invoke-Demo.ps1`: start, stop, and demonstrate the actual services.
- `Test-Demo.ps1`: 25 HTTP/SQL assertions, including ten concurrent receiver requests, one business effect, matching receipts, conflicts, authentication, source isolation and durable outgoing persistence. These checks have not run yet.
- Remote SQL access was verified separately using a read-only query; connectivity to master does not establish application database readiness.
- The hosted CI workflow is prepared; a successful remote run must be inspected before reporting hosted CI success.

## Design decisions and next work

The Unit 3 target architecture originally described handling unique-key races by catching insert conflicts. This implementation takes serializable update/range locks before inserting while retaining the unique database key. The purpose is the same: serialize decisions for one identity and preserve exactly one committed order. The prepared concurrent-request test must verify this against SQL Server.

Full rollback fault injection, durable retry schedules, authenticated manual recovery, lost-acknowledgement trials, baseline comparison and performance measurement remain later work. The unit test suite does not establish these properties.

Available student time is ten hours before the September 30 deadline. Review and recording take precedence over optional UI, machinery simulation or additional frameworks. The submitted video must show actual working behavior; the recording guide is a rehearsal aid rather than evidence of completed execution.
