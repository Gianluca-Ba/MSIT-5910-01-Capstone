# Unit 7: system testing and maintenance

Local review package, 11 October 2026. No Unit 7 release or remote publication is claimed.

## Verified results

| Check | Result |
|---|---|
| Unit tests | 54 passed |
| Core HTTP/SQL integration | 25 assertions passed |
| Dashboard integration | 20 assertions passed |
| Customization | 15 checks passed |
| Automatic sender | 10 completed messages: 7 acknowledgements, 3 expected rejections; control checks passed |
| Repeated workload | Four bursts of 50 valid orders; 200 acknowledged |
| Measured scenarios | 320/320 expected outcomes matched, including 40 each duplicate, conflict and invalid requests |
| Custom validation | Three batches of 1000; each 700 valid and 300 invalid, all matched |
| Receiver interruption and restart | RecoveryRequired recorded without a receipt and preserved after restart |

## Corrected test defect

The first interruption test stopped during process verification before injecting a fault. Windows PowerShell 5.1 retained the deserialized process list inside an array wrapper. Removing the redundant wrapper in `Test-DeliveryFailure.ps1` restored enumeration while retaining process-ID and start-time verification. The original failure is in `evidence/Test-DeliveryFailure.txt`; the successful rerun is in `evidence/delivery-failure-retest.txt`. The stale message promising Unit 5 automatic requeue was also corrected.

## Evidence and boundaries

`evidence/summary.json` is derived from the CSV records. `method.json` records workload and timing boundaries. HTTP duration covers request transmission through response-body receipt; observed acknowledgement includes queue and polling overhead. Median valid submission was 1.412 ms, median observed acknowledgement 25.537 s, and throughput approximately 0.989–0.993 acknowledged orders/s.

This is bounded local testing, not a production-capacity, long-duration soak, independent user-acceptance, or disaster-recovery certification. Custom-message validation is ERP-only. The interruption test does not prove post-commit lost-acknowledgement recovery. Automatic retry and audited recovery remain unimplemented.

Unit 6 screenshots retain their original dates and support the consolidated report. `unit7-tests.png` captures actual Unit 7 logs in a local evidence viewer. `architecture.png` is an explanatory diagram, not a runtime screenshot.

## Reproduction

Use the initialized laboratory databases and private local credentials. Do not reset them. Run the existing unit and integration scripts against the local services. The interruption test deliberately stops the verified WMS demo process and restores the services; run it only during an isolated test window.

For fresh measurements, use `Measure-Unit6.ps1 -Trials 4 -OrdersPerTrial 50 -EvidenceDirectory <absolute-new-directory>`, then pass the same directory to `Summarize-Unit6.py`. Archive previous evidence before reusing a destination. The optional destination preserves the original Unit 6 measurements.

## Review and maintenance

The Word submissions provide the test plan, defect record, corrective/adaptive/perfective/preventive maintenance matrix, monitoring schedule, configuration controls, risks, and full Chapters 1–6. Review the documents and script change before committing or publishing. Proposed backup restoration, supported-host deployment, operational monitoring, and recovery workflows are not reported as completed work.
