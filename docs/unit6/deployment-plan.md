# Unit 6 deployment and configuration plan

Selected scope: one Windows laboratory host with ERP, WMS and the existing local SQL databases. Current execution is verified; production service hosting, backup restoration, external HTTPS and cloud/container deployment remain proposed work.

## Configuration baseline

| Item | Value or handling |
| --- | --- |
| SDK | 10.0.401, latestPatch roll-forward in global.json |
| Runtime for published packages | Compatible .NET 10 ASP.NET Core runtime |
| Scripts | Windows PowerShell 5.1+ |
| SQL | Existing `.\SQLDEVELOPER`; separate CapstoneErp/CapstoneWms |
| Database credentials | Restricted per-service logins; private ConnectionStrings__Database |
| API keys | Distinct Auth__Clients__demo per service; ERP Delivery__Keys__demo matches WMS |
| Addresses | ERP 127.0.0.1:5080; WMS 127.0.0.1:5081; loopback only |
| Delivery | Delivery__Enabled=true; one ERP worker |
| Nonsecret settings | settings.xml in each service's working directory; environment added afterward |
| Local secret storage | Windows-user-bound encrypted .local/secrets.xml; excluded from Git |

## Existing environment

Do not run initialization again. Stop recorded processes before rebuilding locked binaries. Run locked restore, Release build and unit tests. Start WMS before ERP using Start-Demo.ps1. Verify `/health` on both ports and the complete HTTP/SQL workflow. The base-schema health check does not prove that automation/customization migrations are present; run their feature checks too.

## Fresh installation

On an authorized fresh host, use Initialize-Demo.ps1 with the target server. It prompts for administrator credentials, creates only the named objects, refuses existing names, and provisions restricted application logins. Inspect partial failures before retrying. Existing older databases use reviewed additive migrations rather than destructive recreation. Re-provision secrets under the actual Windows account used for startup; encrypted local values are not portable between identities.

## Packaging and smoke test

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet publish src/Capstone.Erp -c Release --no-restore -o artifacts/package/erp
dotnet publish src/Capstone.Wms -c Release --no-restore -o artifacts/package/wms
```

Extract services into separate directories and set private per-process environment values before running `dotnet Capstone.Wms.dll` and `dotnet Capstone.Erp.dll` from their respective directories. Verify dashboard assets and settings.xml were included. The smoke test must confirm acknowledgement, duplicate receipt preservation, conflict refusal, and SQL counts. Hosted build/test/package preparation is not automatic deployment to this host.

## Upgrade and rollback

Before replacing processes, record revision, configuration and schema versions; preserve the old package and back up both databases. Review migration compatibility, stop old processes, deploy and smoke-test. If validation fails, restore compatible previous binaries and configuration. Binary rollback does not undo schema changes. Backup/restore rehearsal and recovery-time measurement remain outstanding and must precede an operational recovery claim.

## Production prerequisites

The current ARM development environment is an observed laboratory arrangement, not a supported SQL production-host recommendation. Select and validate a vendor-supported SQL host. Add service supervision, startup recovery, log retention and alerts. Network exposure requires HTTPS, a reviewed authentication model and changes to the loopback guard. Replace lab SQL certificate trust with validated certificates. Add audited recovery before operational adoption; do not scale ERP instances without a safe pending-record claim mechanism.
