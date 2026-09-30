# Unit 5 verification evidence

Local verification performed on 29 September 2026. Publication is pending author review and approval.

## Results

- Baseline: 44 passing unit tests.
- Regression before correction: maximum-quantity conflict injection failed; all 148 conflict samples contained quantity 100001 and were rejected by input validation.
- Correction: change 100000 to 99999; otherwise increment the quantity. This preserves a valid but conflicting business payload.
- Final: 54 passing tests, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- Fresh integration check: acknowledged order, identical resend HTTP 200 with original receipt, changed quantity HTTP 409, exactly one SQL order and receipt.

Coverage is measured by Coverlet across the instrumented Capstone.Core assembly, including generated code: 176/317 lines (55.52%) and 221/368 branches (60.05%). OrderRules, DeliveryOutcome and AutomationPlan each have 100% measured line and branch coverage. MessageDefinitions has 98.36% line and 81.69% branch coverage. SQL and middleware need separate integration testing; the ERP and WMS executable assemblies are not covered by this unit run. Coverage is not a proof of defect absence.

## Reproduction

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore --collect "XPlat Code Coverage" --logger trx --results-directory artifacts/unit5
```

For the full service build, stop the demo first to release its DLL locks, build Release, restart using Start-Demo.ps1, then run Invoke-Demo.ps1. Do not rerun database initialization. Integration checks add synthetic records to the existing local databases.

## Evidence map

| Requirement | Evidence |
| --- | --- |
| Core validation and idempotency | 01-core-output.png; live-flow.txt; source files |
| Test-driven issue resolution | 02-regression.png; regression-before.txt; Unit5RegressionTests.cs |
| Unit execution and coverage | 03-tests-coverage.png; unit-tests.txt; coverage.cobertura.xml; coverage-summary.txt |
| Milestone management | Local annotated tags and release-notes.md; remote publication pending approval |

The screenshots show a local evidence viewer displaying exact source excerpts and captured logs. They are not screenshots of Visual Studio. The coverage table is calculated from the accompanying Cobertura report. Existing 1,000-message evidence remains separately dated September 24; it is not represented as a new run.
