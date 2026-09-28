# Local demonstration verification — 24 September 2026

The demonstration uses the local SQL Server instance `.\SQLDEVELOPER`, with `CapstoneErp` and `CapstoneWms`. All inputs are synthetic. The earlier remote SQL host is not required.

## ERP to WMS delivery

Run: `739868ec-e63c-40fd-b09f-8a36821d6fff`.

Configuration: 1,000 messages, 30% intentional errors, 100 ms submission interval, seed 5910. Started at 01:11:29 UTC and completed at 01:23:20 UTC on 24 September 2026. Completion includes warehouse processing after submission finishes.

| Observed outcome | Count |
|---|---:|
| Acknowledged | 700 |
| Validation rejected | 152 |
| Conflict rejected | 148 |
| Unexpected outcomes | 0 |
| Total messages | 1,000 |

Every message matched its expected outcome. A direct SQL check, restricted to the acknowledged order IDs from this run and the demo source, found **700 accepted warehouse orders and 700 acceptance receipts**. These are run-specific counts, not totals across the demonstration databases.

## Custom message validation

Batch: `3ee7a4cf-7e7d-4d9b-ad02-1e838d90cba3`, created at 01:12:26 UTC on 24 September 2026.

Definition: `GuideWarehouseNotice`, version 1. Configuration: 1,000 messages, 30% intentional errors, seed 5910.

| Observed outcome | Count |
|---|---:|
| Valid | 700 |
| Rejected | 300 |
| Unexpected outcomes | 0 |

This is a separate ERP validation batch. It does not submit warehouse orders or demonstrate delivery of custom message types.

## Screenshot evidence and limits

All five screenshots in the [user guide](user-guide.md) were retaken from the local environment on 24 September 2026. The automatic sender screenshot shows the completed delivery run; the custom validation screenshot shows the separate custom batch. The order evidence screenshot shows a delivered order with one warehouse order and one receipt after a changed-quantity request was rejected with HTTP 409.

Intentional error injection verifies controlled scenarios. Its 30% rate is not an estimate of real warehouse failures. The runs demonstrate the recorded cases and do not establish production throughput, reliability under arbitrary failures, or support for custom message delivery.
