. "$PSScriptRoot/Common.ps1"
$secrets=Get-DemoSecrets
$erpHeaders=@{'X-Api-Key'=(Unprotect $secrets.ErpKey)}
$wmsHeaders=@{'X-Api-Key'=(Unprotect $secrets.WmsKey)}
$order=@{version=1;orderId=[Guid]::NewGuid().ToString();sku='DEMO-PALLET-01';quantity=5;correlationId=[Guid]::NewGuid().ToString()}
$body=$order|ConvertTo-Json -Compress
$result=Invoke-RestMethod 'http://127.0.0.1:5080/api/orders' -Method Post -Headers $erpHeaders -ContentType 'application/json' -Body $body
Write-Output "Submitted order $($order.orderId): $($result.status)"
for($i=0;$i -lt 30;$i++){
    $status=Invoke-RestMethod "http://127.0.0.1:5080/api/orders/$($order.orderId)" -Headers $erpHeaders
    if($status.status -ne 'Pending'){break};Start-Sleep -Milliseconds 500
}
if($status.status -ne 'Acknowledged'){throw "Expected Acknowledged, observed $($status.status)"}
$status|ConvertTo-Json -Depth 5
$duplicate=Invoke-WebRequest 'http://127.0.0.1:5081/api/acceptances' -UseBasicParsing -Method Post -Headers $wmsHeaders -ContentType 'application/json' -Body $body
$receipt=$duplicate.Content|ConvertFrom-Json
if($duplicate.StatusCode -ne 200 -or $receipt.receiptId -ne $status.receipt.receiptId){throw 'Duplicate did not return the original receipt'}
Write-Output "Duplicate returned HTTP 200 and original receipt $($receipt.receiptId)"
$order.quantity=6
try{Invoke-WebRequest 'http://127.0.0.1:5081/api/acceptances' -UseBasicParsing -Method Post -Headers $wmsHeaders -ContentType 'application/json' -Body ($order|ConvertTo-Json) | Out-Null;throw 'Expected conflict'}
catch{if([int]$_.Exception.Response.StatusCode -ne 409){throw};Write-Output 'Changed quantity returned HTTP 409'}
$c=New-Object System.Data.SqlClient.SqlConnection((Unprotect $secrets.WmsConnection));$c.Open()
try{
    $cmd=$c.CreateCommand();$cmd.CommandText="SELECT (SELECT COUNT(*) FROM dbo.AcceptedOrder WHERE SourceId='demo' AND OrderId=@id),(SELECT COUNT(*) FROM dbo.AcceptanceReceipt WHERE SourceId='demo' AND OrderId=@id)"
    [void]$cmd.Parameters.AddWithValue('@id',[Guid]$order.orderId);$r=$cmd.ExecuteReader();[void]$r.Read()
    if($r.GetInt32(0) -ne 1 -or $r.GetInt32(1) -ne 1){throw 'Database invariant failed'}
    Write-Output 'SQL assertion passed: exactly one accepted order and one receipt.';$r.Close()
}finally{$c.Dispose()}
