# Automatic message lab

## Setup

Fresh initialization installs the additive automation schema. For an existing ERP database, run `scripts/Update-AutomationDatabase.ps1` with a SQL administrator credential once, then restart the services. The migration is repeatable and preserves existing orders. It adds 1,000 synthetic SKU/quantity template rows, AutomationRun, and AutomationMessage. The ERP application receives SELECT on templates and SELECT/INSERT/UPDATE on its run tables; it receives no new administrative or DELETE rights.

## Use

Connect to the dashboard, select 10–1,000 messages, an injected error percentage (0–80), a send interval (100–5,000 ms), and a seed. Arm persists every generated payload but sends nothing. Run starts the selected armed batch. Only one batch can be Running, Draining, or Stopping at a time. Stop/disarm cancels queued messages; an already claimed request may complete. Previously submitted ERP orders continue through the normal outbox worker.

The default batch contains 1,000 messages with 300 intended errors. SKU templates are shuffled without replacement, quantities are independently sampled from the template pool, and new order/correlation UUIDs are generated. The seed reproduces scenario placement and business field combinations, not UUIDs. Change the seed for a different scenario mix. The first message is valid. Error cases contain either invalid quantity zero or a changed quantity against an earlier valid order identity. There are no intentional network outages and no automatic retries. Duplicates remain available in the manual receiver checks.

The sender calls the real authenticated ERP submission endpoint. Valid requests enter the normal durable outbox and reach WMS. Invalid quantities and conflicts are analyzed and rejected by ERP before delivery; they must not be described as WMS failures. If a preceding valid submission fails unexpectedly, a later conflict may no longer yield its intended outcome: the mismatch is retained rather than manufactured into a pass.

## Observations and audit

The live map refreshes every two seconds. Tiles show Queued, Sending/AwaitingDelivery, Acknowledged, expected rejection, unexpected/unknown, or Cancelled. Click a tile for its persisted input and results. Recent runs remain available across reloads, disconnections and process restarts.

Each message stores its template ID, scenario, expected outcome, complete JSON payload, send-start time, HTTP-response observation time, final-outcome observation time, HTTP code, response text, final state, matching-result flag and receipt when known. All database timestamps are UTC. AnalyzedAt is when the sender records the HTTP result, not a measurement of the internal validation instant. CompletedAt is when the observer persists the terminal outcome, not an exact WMS execution timestamp. HTTP 202 means submitted, not acknowledged.

The sender can enqueue faster than the existing worker's roughly one-order-per-second loop. Draining means all requests have been sent but some outcomes remain pending. A 1,000-message run at 30% injected errors can take about 12 minutes or longer to drain; this is not a throughput benchmark. The progress bar includes cancelled messages as terminal and labels this explicitly. Expected rejections are counted separately from unexpected or uncertain outcomes.

After service restart, previously Running/Draining runs become Interrupted, unsent messages are cancelled, and in-flight messages become Unknown. Already accepted orders can still be reconciled from the outbox. No interrupted batch automatically resumes sending. Armed batches stay armed until the user runs or disarms them. Stopping runs finish reconciliation without sending more. Unknown never means confirmed rejection or confirmed loss.

The worker assumes a single ERP service instance, matching the existing outbox implementation. The generated workload and percentages describe a controlled experiment, not real industrial failure distributions. Payloads contain synthetic data only. Only recent run summaries are listed by default; all records remain in SQL.
