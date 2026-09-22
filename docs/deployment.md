# Unit 4 setup and deployment

## Requirements

Windows PowerShell 5.1 or later for these demo scripts, the .NET SDK pinned in `global.json`, and an authorized SQL Server instance. Services run on ARM64 Windows; SQL runs on a separate x64 host. No production ERP, machinery, or workplace data is required. Open `ReliableErpWms.slnx` in a compatible Visual Studio installation, or use the .NET CLI. Visual Studio installation itself is not part of the delivered software.

## Initialize once

From the repository root, run:

```powershell
./scripts/Initialize-Demo.ps1 -Server 'tcp:YOUR-SQL-HOST,YOUR-PORT'
```

The script prompts for a SQL administrator credential. It creates only `CapstoneErp`, `CapstoneWms`, `CapstoneErpApp`, and `CapstoneWmsApp`; it refuses to touch existing objects with those names. It applies the two versioned schema scripts. Application logins have SELECT/INSERT on their own tables and, for ERP, UPDATE on the outbox. They cannot administer the server or delete tables. Administrator credentials are not retained. A setup failure may leave newly created objects; inspect them before any rerun rather than dropping data automatically.

Generated connection strings and API keys are stored as Windows user-bound encrypted values in ignored `.local/secrets.xml`. Run startup as the same Windows user. Keep that file private and out of recordings. The nonsecret XML configuration provides loopback URLs and the worker switch; process environment variables override it. Authentication maps API keys to sources on the server, never from submitted payloads.

SQL transport uses encryption with certificate trust enabled for this private laboratory host. Deployment outside the lab needs a validated server certificate and `TrustServerCertificate=False`. Both service APIs bind to loopback and reject nonloopback clients; network deployment requires HTTPS and a reviewed authentication arrangement.

## Build run and demonstrate

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --logger trx --results-directory artifacts/tests
./scripts/Start-Demo.ps1 -NoBuild
./scripts/Invoke-Demo.ps1
./scripts/Test-Demo.ps1
./scripts/Test-DeliveryFailure.ps1
./scripts/Stop-Demo.ps1
```

If dotnet is not on PATH, use its installed absolute path. The startup scripts also find a portable SDK at `../tools/dotnet/dotnet.exe`. Start creates hidden background service processes, records their IDs and start times, checks database readiness, and writes local logs. Stop terminates only matching recorded processes. Each demo run uses a new order ID and retains its records; there is no destructive reset shortcut.

## Package deployment

The workflow restores locked packages, builds, tests, and publishes framework-dependent ERP and WMS directories. Its artifact includes database scripts, these instructions, and `REVISION.txt`. A successful artifact is continuous delivery preparation, not automatic deployment to the developer's computer. Hosted CI does not access the LAN database.

For manual artifact deployment, install the corresponding .NET 10 ASP.NET Core runtime, extract the package, and set each process's `ConnectionStrings__Database` and `Auth__Clients__demo` from private configuration. ERP additionally needs `Delivery__Keys__demo` equal to the WMS key. Start `dotnet Capstone.Wms.dll` from the `wms` directory and `dotnet Capstone.Erp.dll` from `erp`, then check `/health` and rerun the demo requests. Do not reapply creation scripts to an existing database. Stop the old processes before starting replacements. An executable rollback does not reverse schema changes.

## Unit 4 limitations

Run one ERP worker. It makes one bounded attempt for each pending message, persists a terminal local outcome, and marks uncertain delivery `RecoveryRequired`. A process crash before outcome persistence leaves the record pending for safe redelivery. Automatic scheduled retries, audited manual requeue, lost-acknowledgement experiments, baseline comparison, and throughput claims belong to subsequent milestones. A failed HTTP call is never treated as proof that the warehouse did not commit.

SQL integration checks run separately from GitHub's unit tests. Synthetic requests drive real code and SQL transactions; they are not a machinery simulator. Test data and receipts remain for inspection.

The optional failure test stops only the recorded WMS demo process, confirms an uncertain delivery is RecoveryRequired, then restarts both services and checks the persisted status. It intentionally retains the failed order. See `unit4-verification.md` for observed results.
