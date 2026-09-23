# Message customization: first milestone

This feature adds a versioned message designer and an ERP validation lab. Custom messages are generated, validated and recorded in ERP SQL. They are **not sent to WMS**, and a valid result is **not a warehouse acknowledgement**. The existing order sender remains the end-to-end ERP–WMS demonstration.

## Setup

For an existing installation, run `scripts/Update-CustomizationDatabase.ps1` with a SQL administrator. It adds definition and evidence tables without modifying order records. Fresh installations apply this migration through `Initialize-Demo.ps1`.

Start the services using `Start-Demo.ps1`. If Windows blocks scripts, use:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Progetti\reliable-erp-wms\scripts\Open-Dashboard.ps1"
```

Paste the copied dashboard API key and click Connect. The key remains in tab memory. Refreshing requires reconnection.

## Use the designer

1. Open **Message designer** in the dashboard.
2. Name the type, for example `WarehouseNotice`. Names are case-sensitive and start with a letter; letters, digits and underscores are allowed, up to 40 characters.
3. Add up to 24 fields. A shared section name groups fields into one JSON object. The starter example contains Header.MessageId, Destination.Area and Details.Quantity.
4. Select text, integer, boolean, identifier or choice. Required means the field cannot be omitted. Present null values are always invalid. Text min/max specifies string length; integer min/max specifies bounds. Choices are comma-separated in the editor (values containing commas require the API). Other types ignore min/max.
5. Click **Save as new version**. Saving the same type name creates the next version; previous definitions cannot be edited or deleted by the application login.
6. Choose a count (1–1000), injected error percentage (0–80), and integer seed. Click **Generate & validate**. This executes a bounded validation batch immediately, without an Arm/Run delivery cycle.
7. Select a colored tile to inspect its payload, field errors, expected validity, actual validity, and recorded UTC time. Green is valid, gold is an expected rejection, red is a mismatch or a rejected manual message.
8. Edit the JSON and click **Validate edited message & record** to test your own input. The selected saved version is shown above. Manual checks have no predetermined expected outcome.
9. **Load recent validation batches** retrieves the latest 20 batches for the authenticated source. Selecting a batch restores its definition version and evidence. Definitions and batches survive service restarts.

Unsaved field edits disable generation and manual validation until saved. Selecting a saved definition replaces the editor contents, so save desired changes first.

## Data and evaluation

The generator uses a seed for business values and randomized fault positions. Identifier values are new GUIDs on every generation. Error counts are the requested percentage times the batch size, rounded to the nearest integer with midpoint rounding away from zero. Consequently very short batches can differ from the requested percentage.

The first injection mode replaces exactly one selected field with null. This provides one known, independently detectable validation fault per invalid message, including optional fields when present. It does not simulate network outages or business conflicts. Broader fault modes and related-field constraints are future work.

All generated messages, injected faults, outcomes, field errors, settings, and timestamps are persisted as a JSON evidence document linked to an immutable definition version. RecordedAt is the validation observation time, not network delivery latency. A success response is returned after SQL persistence succeeds. A lost response can leave an already saved batch: check recent history before repeating a request. Requests do not yet have an idempotency key.

Validation rejects unknown sections/fields, wrong section shapes, missing required fields, wrong value types, invalid choices, non-GUID or empty identifiers, and values outside configured bounds. Text length uses .NET UTF-16 string length. Generated text uses ASCII uppercase letters. Integer bounds are limited to ±1,000,000; text to 256 characters; choice lists to 20 entries of 64 characters each. Customization requests are limited to 64 KiB; existing order endpoints retain their 4 KiB limit.

No user code, SQL or expressions execute. All SQL values are parameterized and definitions/evidence are scoped to the authenticated source. The ERP application user has SELECT/INSERT only on the new tables. Raw evidence remains in the private database, not Git. Use synthetic data.

## Verification and next steps

`MessageDefinitionTests` includes independently authored valid/invalid fixtures and generation checks. `scripts/Test-Customization.ps1` verifies immutable versions, a 1000-message batch with 300 faults, persistence, source isolation and rejected invalid settings through real HTTP and SQL. `Test-Dashboard.ps1` covers the original order flow.

Next milestones: add more deliberate fault modes, decimal/date fields and repeating sections as required; define actual business handlers before routing new types through the outbox and WMS; then add live delivery tracking for those operations. Generated validation results alone do not establish real-world failure rates or warehouse business correctness.
