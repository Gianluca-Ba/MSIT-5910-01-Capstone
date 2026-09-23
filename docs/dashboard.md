# Local verification dashboard

The ERP service serves the dashboard at http://127.0.0.1:5080/. It uses HTML, CSS and browser JavaScript with the existing ASP.NET Core application. There is no separate Node, React or frontend build dependency.

## Start and connect

From the repository root:

```powershell
./scripts/Start-Demo.ps1
./scripts/Open-Dashboard.ps1
```

If the services are already running, only run Open-Dashboard. After code changes, run Stop-Demo before rebuilding and starting. Open-Dashboard decrypts the existing ERP key for the current Windows user, copies it to the clipboard, and opens the page. Paste into the masked key field and click Connect. Clear your clipboard afterward. The dashboard keeps the key in memory only, not in URLs, browser storage, request history, or HTML. Refreshing the page requires reconnecting. The helper supports `-NoBrowser` when the page is already open.

## Demonstrate input, decision and output

1. Connect. The service health indicator checks ERP and its SQL database; it does not claim WMS is healthy.
2. Submit the generated order with quantity 5. Inspect the exact POST payload and HTTP 202 response.
3. Observe the persisted state transition from Pending to Acknowledged. The page polls for at most 30 seconds. Use Refresh evidence afterward if necessary.
4. Verify WMS reports one accepted order and one receipt, matching the ERP receipt. These values are queried from SQL through WMS, scoped to the authenticated source and selected order.
5. Click Resend identical order. A fresh correlation ID is tracing metadata; immutable business content remains identical. WMS should return HTTP 200 with the original receipt.
6. Click Send quantity conflict. The dashboard sends a changed quantity for the selected persisted order. WMS should return HTTP 409; the SQL-backed quantity and counts should remain unchanged.
7. Click New order, enter quantity 0, and submit. Observe backend HTTP 400 without a persisted order. This demonstration intentionally allows invalid quantities through the form.
8. Use Look up this order for an existing UUID. Status is scoped to the connected source. Selecting an order and editing the form are separate: receiver checks always use the selected persisted order shown in the decision card.

The last 20 exchanges are held in tab memory. Select a request row to inspect its exact response body. Automatic polling records exchanges without replacing the main request/response display. This is a session inspection view, not a durable audit log. No complete database order list is exposed.

## Interpretation and scope

The interface explains backend states; it implements no independent acceptance logic. Pending means ERP has persisted outgoing intent. Acknowledged means a valid receipt is persisted at ERP. Rejected reflects an explicit business rejection. RecoveryRequired means acceptance is uncertain, not necessarily absent at WMS. Unavailable evidence displays dashes rather than invented zero counts. Receiver checks bypass the ERP outbox for verification and do not requeue or repair an ERP order.

The existing Test-DeliveryFailure script can demonstrate a real receiver outage. There is no dashboard stop-service button or automatic recovery. Only synthetic records should be entered. The HTTP services remain loopback-only, all business APIs require the existing API key, and the ERP gateway uses only its configured source-specific WMS key and loopback target. The public page contains no keys or business records. Content security policy restricts scripts and connections to the same origin.

## Verification

```powershell
./scripts/Test-Dashboard.ps1
./scripts/Test-Demo.ps1
dotnet test -c Release --no-build
```

Dashboard integration checks cover static assets, secret absence, authentication, source isolation, durable delivery, duplicate receipt preservation, conflicts, validation, and actual SQL-backed evidence. The publisher includes the ERP wwwroot assets automatically. No new SQL grants, tables, or third-party packages are required.
