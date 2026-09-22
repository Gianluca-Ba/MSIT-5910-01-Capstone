# Proposed system requirements and design

Status: Unit 3 target specification, retained for traceability. Unit 4 now implements the initial subset described in `unit4-status.md`. Scheduled retries, manual recovery, fault experiments, and performance targets below remain future work.

## Design decisions

Two C# service processes communicate over an HTTP contract. Separate ERP and WMS databases may share one local SQL Server instance, but each service has its own database permissions and transactions. There is no distributed transaction across the databases. This intentional boundary exposes delivery uncertainty while keeping the implementation feasible. A SQL outbox provides durable publication without adding a message-broker administration requirement.

The first workflow is immutable order creation and acceptance. Messages contain a contract version, stable order identifier, SKU, positive quantity, and correlation identifier. Authentication determines the source identity; clients cannot impersonate another source through a payload field. The uniqueness key is the authenticated source plus stable order identifier. Canonical business fields are stored and compared so that harmless JSON formatting changes do not produce false conflicts. Correlation identifiers aid tracing and never grant access.

## Functional requirements and validation

| ID | Requirement | Planned verification |
| --- | --- | --- |
| FR01 | Validate version, identity, SKU, quantity, and size before accepting a source order. | Invalid, missing, boundary, and oversized inputs. |
| FR02 | Commit the source order and delivery intention together or neither. | SQL rollback and process-interruption integration tests. |
| FR03 | Deliver due records with their original identity and immutable content. | Inspect captured requests across retries and restarts. |
| FR04 | Atomically persist one WMS order and its acceptance receipt. | Transaction rollback and concurrent duplicate submissions. |
| FR05 | Return the original outcome for matching duplicates; reject altered-content duplicates. | Sequential and concurrent same-ID tests. |
| FR06 | Distinguish retryable failures, business rejection, unknown outcome, and exhausted attempts. | State-transition and HTTP response classification tests. |
| FR07 | Allow authorized recovery to requeue the original identity while preserving attempt history. | Permission, race, and audit assertions. |
| FR08 | Expose source-authorized status and correlate attempts across services. | Cross-source access denial and reconciliation assertions. |

## Nonfunctional requirements and tradeoffs

| ID | Requirement | Acceptance evidence or limit |
| --- | --- | --- |
| NFR01 Reliability | Zero duplicate committed WMS effects for a source/order key in the planned experiments. | Direct SQL assertions; no inference from response counts alone. |
| NFR02 Accountability | Every committed delivery intention remains accepted, pending, rejected, or explicitly awaiting recovery. | Reconcile an independent submitted-ID manifest against both databases. |
| NFR03 Recovery | Proposed 60-second completion target after restoration for the defined 100-order workload. | Pilot before freezing protocol; separate automatic recovery from operator-assisted recovery. |
| NFR04 Performance | Record acceptance latency and retry overhead without promising an untested throughput threshold. | Matched baseline and improved runs on the same machine. |
| NFR05 Security | Authenticate operations, authorize source-specific access, and exclude secrets from artifacts. | Negative authorization tests, configuration review, and publication checks. |
| NFR06 Usability | Return stable status/error codes with correlation identifiers and recovery instructions. | Scripted operator walkthrough; no unsupported user-satisfaction claim. |
| NFR07 Scalability | Keep processing bounded and index due-work and identity lookups. | Single worker initially; multi-worker scale-out is outside the assessed deployment. |
| NFR08 Reproducibility | Pin dependencies and provide setup, run, reset, and test instructions. | Clean-checkout exercise before final release. |

## Module specifications

| Module | Input | Output | Method and invariant |
| --- | --- | --- | --- |
| Submission API | Authenticated source and order fields | Source order ID and pending status, or validation failure | Validate; insert source order and outbox in one local SQL transaction. Repeated source submissions use the same stable key and conflict rules. |
| Outbox worker | Due immutable delivery records | HTTP requests, persisted attempts, updated delivery states | One worker selects due records in deterministic due-time/key order. Persist next-attempt time and count; do not retain a database transaction across HTTP. |
| Retry policy | Failure class, attempt count, current time | Retry time, terminal rejection, or recovery-required decision | Pure decision function with injectable clock. Bounded exponential delay and maximum attempts. Unknown delivery is never labeled business rejection. |
| WMS acceptance API | Authenticated source, order identity, business fields | Accepted receipt, original duplicate outcome, or conflict | Parameterized SQL; unique source/order key; receipt and order in one transaction. On concurrent uniqueness conflict, roll back and read the committed receipt before comparing fields. |
| Status and recovery API | Authorized principal, source/order key, recovery reason | Sanitized status or audited requeue result | Check function and object access. Requeue only eligible records through conditional updates, retaining the identity and complete history. |
| Fault runner and assertions | Manifest, scenario, seed, baseline/improved mode | Raw results and invariant failures | Real service requests and SQL checks in disposable environments. Lost-response fault occurs after the receiver commit. No predetermined success outputs. |

## Persisted state and recovery

ERP stores SourceOrder, OutboxMessage, and DeliveryAttempt. WMS stores AcceptedOrder and AcceptanceReceipt. A receipt contains the canonical request fields and stored response; its lifetime covers the entire retry and recovery horizon. Deleting a receipt while an old request can return would undermine deduplication, so no receipt-expiry feature is included.

Delivery states are Pending, RetryScheduled, Acknowledged, Rejected, and RecoveryRequired. Acknowledged requires an acceptance response consistent with the original identity. Timeouts and connection failures can transition to RetryScheduled or RecoveryRequired, never to Rejected merely because delivery is uncertain. Manual recovery starts a new bounded retry cycle under the same identity and records the actor and reason.

A crash after WMS commit but before ERP acknowledgement causes redelivery. The stored receipt resolves the uncertainty without a second business effect. A crash after reading an outbox record is handled by reading the persisted pending state on restart. With one worker, duplicate delivery caused by restart is acceptable because the receiver enforces idempotency. Concurrency tests still exercise the receiver independently.

## Security and ethical specifications

For the local prototype, separately configured high-entropy credentials identify the submitting service and recovery operator; secrets remain outside Git. Source mappings and operation permissions are configured server-side. Authentication is a prerequisite to all status, delivery, and recovery operations, including duplicates. Network deployment requires HTTPS; local faults target only the test environment. XML configuration parsing disables external entity resolution.

Synthetic order fields contain no employee, customer, or workplace information. Logs retain identifiers, status, timing, and sanitized failure categories. Recovery does not rewrite payloads, hide previous failures, or privilege an order based on personal characteristics. Due-time scheduling and bounded work reduce avoidable starvation; no untested fairness guarantee is claimed. A future participant study would require informed consent before collecting responses.

OWASP (2023) emphasizes object-level authorization at endpoints accepting record identifiers. This informs source-scoped status and recovery checks. Microsoft (n.d.) recommends differentiating transient failures from failures unlikely to benefit from retry and limiting repeated attempts. These principles support the proposed retry classifier without making Azure a project dependency.

## Evaluation clarification carried forward from Unit 2

The two variants use matched business contracts, hardware, inputs, and fault schedules. The baseline omits the reliability mechanisms under study but retains input validation and authentication. Its limitations are disclosed; it is not presented as a representative commercial ERP implementation.

Six scenarios each receive ten runs of 100 distinct logical orders per variant. Retries are additional attempts, not additional logical orders. The recovery clock starts when the fault is removed. An exhausted retry cycle cannot satisfy an automatic recovery target without intervention; therefore report automatic and operator-assisted outcomes separately, including time spent waiting for intervention. This resolves an ambiguity in the Unit 2 proposal rather than promising recovery after arbitrarily long outages.

## References

Microsoft. (n.d.). *Retry pattern*. https://learn.microsoft.com/en-us/azure/architecture/patterns/retry

OWASP Foundation. (2023). *API1:2023 Broken object level authorization*. https://api-security.owasp.org/editions/2023/en/0xa1-broken-object-level-authorization/
