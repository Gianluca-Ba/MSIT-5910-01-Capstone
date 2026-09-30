# Proposed milestone releases

These notes are prepared locally. No GitHub release has been published for Unit 5.

## v0.4.0-unit4 — Working integration and dashboard baseline

Target: deaf34a. Existing implementation with ERP/WMS services, local SQL deployment, dashboard, automatic sender, versioned custom-message validation and illustrated documentation. September 24 evidence records 1,000 delivery messages and a separate 1,000-message validation batch. This tag is being assigned retrospectively; it was not created on the original commit date.

## v0.5.0-unit5 — Core logic verification and coverage

Fix conflict generation at the maximum valid order quantity so expected conflicts remain valid input. Add 10 unit cases covering that regression, incomplete receipts, immutable identity/version changes, invalid run settings and insufficient templates. The resulting 54 tests pass. Add Coverlet collection to the existing CI test command; retain Cobertura and TRX in the test artifact.

Validation: Release build has 0 warnings/errors. Core assembly coverage is 55.52% of lines and 60.05% of branches. The fresh HTTP/SQL demonstration confirms acknowledgement, duplicate receipt reuse, HTTP 409 on changed content and one order/receipt pair. Local CI configuration has not yet run on GitHub for this commit.

Limitations: single delivery worker; no production throughput claim; custom message definitions validate in ERP and do not execute warehouse work. SQL and middleware are outside the unit-test exercise. Local secrets and database content are not release assets.

## Approval and publication sequence

Review the code diff, evidence, Word report and these notes. After approval, push only the Unit 5 branch and the two named tags, publish the corresponding GitHub releases, verify anonymous access to branch/tags/releases and the hosted CI result, then update the report's publication-status paragraph. Do not merge into main without review.
