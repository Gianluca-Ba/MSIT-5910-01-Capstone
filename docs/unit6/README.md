# Unit 6 integration, evaluation and deployment

Evaluation date: 4 October 2026. Application baseline: `376031bac1c4c6ed333674cbe8084048eeb363d8`. Unit 6 work is on local branch `feature/unit6-evaluation`; publication requires author review. No application acceptance behavior was changed for these measurements.

## Results

| Measurement | Sample | Result |
| --- | --- | --- |
| ERP submission HTTP response | 90 | median 1.462 ms; nearest-rank p95 3.623 ms |
| Observed submission-to-acknowledgement | 90 | median 15.588 s; p95 29.336 s |
| Acknowledged throughput | 3 bursts of 30 | 0.984, 0.992, 0.986 orders/s |
| Expected/actual agreement | 180 order scenarios | 180 matched; 0 mismatches |
| Custom validation | 3 batches of 1000 | 700 valid + 300 invalid each; 3000 matched |
| Existing regression checks | latest local run | 54 unit tests, 25 core HTTP/SQL assertions, 20 dashboard assertions, 15 customization assertions; all passed |

Automation lifecycle checks also passed: arming without transmission, overlapping-run refusal, disarm, active stop, persisted audit fields, seven acknowledgements and three expected rejections for the ten-message completed run. Local `dotnet publish` succeeded for both services. Neither a new cloud deployment nor Unit 6 hosted CI execution is claimed.

## Measurement method

Use the initialized local SQL environment and a single ERP instance. `Measure-Unit6.ps1` refuses to start when a pending outbox workload or active automated run exists. Do not start another workload during measurement. Three warm-up orders are retained but excluded. Each of three trials submits 30 new orders quickly, then polls for terminal state with 100 ms sleeps between sequential polling rounds. No changes to the one-second worker cadence were made. SQL assertions run after the timed window.

HTTP time uses Stopwatch around request transmission and response-body receipt, excluding client JSON parsing. End-to-end time starts before submission and ends at terminal-state observation, including queue waiting and polling overhead. Throughput is acknowledgements divided by the burst observation window. Duplicate/conflict checks call WMS directly; invalid quantities call ERP. They are outside the throughput window. Agreement also checks SQL counts, no persisted ERP order for invalid input, and original receipt equality for duplicates. Raw records preserve expected and actual status, SQL counts, payloads and identifiers. No timeout or unmatched samples occurred in this run.

The custom measurements use a saved three-section definition and seeds 6001, 6002, 6003 after a ten-message warm-up. Generation calls measure the combined generation, validation, SQL evidence persistence, serialization and HTTP response transfer. These are not WMS delivery times. Injected faults replace one field with null. UUIDs are not reproducible from the business-data seed.

The harness preflight originally referred to `AutomationRun.Status`; inspection identified the actual column `State`. This was corrected before any measured workload ran. The initial failed preflight is not a service-performance result. PowerShell CSV numbers use the host's decimal comma; the summarizer handles this explicitly.

## Reproduce

From the repository root, after starting the existing initialized demo:

```powershell
./scripts/Measure-Unit6.ps1 -Trials 3 -OrdersPerTrial 30
python ./scripts/Summarize-Unit6.py
./scripts/Test-Demo.ps1
./scripts/Test-Dashboard.ps1
./scripts/Test-Customization.ps1
./scripts/Test-Automation.ps1
```

The Python summarizer uses standard-library modules only. Re-running the harness adds synthetic SQL records and replaces the local measurement CSV outputs; preserve a copy when comparing experiments. Do not reinitialize databases for an evaluation run. Credentials are read privately through Common.ps1 and never included in evidence.

## Evidence and interpretation

- `evidence/requests.csv`, `trials.csv`, `custom-validation.csv`, `method.json`, `summary.json`: measurement data and calculation inputs.
- `evidence/*tests.txt`, `integration.txt`, `live-flow.txt`: actual verification logs.
- `evidence/code-*.png`, `output.png`, `results.png`: local evidence-viewer screenshots of source excerpts, execution logs and calculated results; not Visual Studio screenshots.
- `evidence/ui-*.png`: authenticated dashboard walkthrough screenshots with synthetic data and no API-key disclosure.
- `evidence/delivery-latency.png`, `http-latency.png`: charts calculated from the measurement records.

The qualitative measure is clarity of state and next action. Inspection covered connection, order acceptance, conflict interpretation, automation history and custom-validation history. Strengths were explanatory state labels, receipts and SQL counts, and separation of expected rejection from uncertainty. Remaining issues were technical API-key setup, long-page navigation, and raw JSON complexity. No recruited participants, satisfaction survey, accessibility audit or independent user study was conducted.

The experiment is descriptive, local, warm and short. The nearly one-order-per-second result is consistent with the worker's fixed delay; burst orders are dependent samples. No production capacity, supported ARM SQL deployment, automatic retry, audited recovery, multiworker safety, or custom-message warehouse dispatch is claimed. See [deployment instructions](../deployment.md) for existing setup and [configuration plan](deployment-plan.md) for Unit 6 operating boundaries.
