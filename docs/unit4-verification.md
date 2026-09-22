# Unit 4 verification evidence

Executed September 22, 2026 against the running loopback APIs and dedicated SQL Server databases. All requests used synthetic capstone data. Application processes used their restricted database logins; administrator access was used only for the approved provisioning.

## Observed results

| Check | Actual result |
| --- | --- |
| Release build | Zero errors and warnings |
| xUnit suite | 28 passed, zero failed, zero skipped |
| Initial hosted CI | Build, tests and service artifacts succeeded for commit 87dc25d |
| `Invoke-Demo.ps1` | ERP Pending then Acknowledged; duplicate HTTP 200 with original receipt; changed quantity HTTP 409; SQL counts one order and one receipt |
| `Test-Demo.ps1` | All 25 HTTP/SQL assertions passed |
| Ten concurrent receiver requests | One HTTP 201, nine HTTP 200; one shared receipt identity and one committed WMS order |
| Real WMS process outage | ERP stored RecoveryRequired and did not claim a receipt |
| Restart after outage | RecoveryRequired survived; both health endpoints returned ready |
| Application account permissions | Neither login has sysadmin, database CONTROL, access to the other capstone database, or order DELETE permission |

End-to-end example retained for inspection: order `da78a927-b8db-42ea-b34b-9f7ba8b0d3c4`, receipt `f14be7a8-47a5-4b79-922e-e3cdd9b84a3a`. The controlled failed-delivery order is `733622fd-66ef-45e4-8772-bff7a6dbc010`. It remains RecoveryRequired by design; Unit 4 does not implement automatic requeue.

Hosted run: https://github.com/Gianluca-Ba/MSIT-5910-01-Capstone/actions/runs/35679108009

## Issues found and resolved

The initial setup failed before connecting because PowerShell interpreted `SqlConnectionStringBuilder.DataSource` as an unsupported keyword. Setup now uses the explicit connection-string dictionary keys (`Data Source`, `Initial Catalog`, and `User ID`). The approved databases and restricted logins were then provisioned successfully.

The initial outage test safely refused to stop the WMS because PowerShell deserialized the recorded timestamp as a date value rather than leaving it as a string. Process ownership checks now compare UTC ticks. The subsequent stop/restart test passed while retaining the ID and start-time safeguard against stopping unrelated processes.

## Evidence limits

The concurrent check is a defined ten-request case, not a throughput benchmark or proof under arbitrary workloads. Successful duplicate and outage tests do not establish all crash/rollback scenarios. Scheduled retries, interrupted acknowledgements, audited manual recovery, transaction fault injection and comparative evaluation remain future milestones. Unit tests, real database checks, hosted CI and actual deployment evidence are reported separately.

To reproduce the successful flow, use `scripts/Invoke-Demo.ps1` and `scripts/Test-Demo.ps1` after startup. `scripts/Test-DeliveryFailure.ps1` deliberately stops only the recorded WMS demo process and restores both services. Run it outside an active recording unless demonstrating that optional scenario.
