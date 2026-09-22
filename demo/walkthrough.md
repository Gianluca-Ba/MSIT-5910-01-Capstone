# API-first walkthrough

Run from the repository root after `scripts/Start-Demo.ps1`. The automated equivalent is `scripts/Invoke-Demo.ps1`. PowerShell setup below loads private keys without printing them.

```powershell
. ./scripts/Common.ps1
$secrets=Get-DemoSecrets
$erpHeaders=@{'X-Api-Key'=(Unprotect $secrets.ErpKey)}
$wmsHeaders=@{'X-Api-Key'=(Unprotect $secrets.WmsKey)}
$order=@{version=1;orderId=[Guid]::NewGuid().ToString();sku='DEMO-PALLET-01';quantity=5;correlationId=[Guid]::NewGuid().ToString()}
$body=$order|ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5080/api/orders -Method Post -Headers $erpHeaders -ContentType application/json -Body $body | ConvertTo-Json -Depth 5
```

Poll status after delivery:

```powershell
Invoke-RestMethod "http://127.0.0.1:5080/api/orders/$($order.orderId)" -Headers $erpHeaders | ConvertTo-Json -Depth 5
```

Send the duplicate directly to the WMS boundary. Expected after successful delivery: HTTP 200 and the original receipt.

```powershell
$repeat=Invoke-WebRequest http://127.0.0.1:5081/api/acceptances -UseBasicParsing -Method Post -Headers $wmsHeaders -ContentType application/json -Body $body
$repeat.StatusCode
$repeat.Content
```

Then change quantity with the same identity. The expected HTTP 409 is a deliberate conflict, not a failed demo.

```powershell
$order.quantity=6
try {
  Invoke-WebRequest http://127.0.0.1:5081/api/acceptances -UseBasicParsing -Method Post -Headers $wmsHeaders -ContentType application/json -Body ($order|ConvertTo-Json)
} catch { [int]$_.Exception.Response.StatusCode }
```

In SSMS, set the order ID from the output and run `database/inspect.sql`. Show one WMS order and receipt after both submissions. Retained requests are synthetic test data. Do not open local secret files during recording.
