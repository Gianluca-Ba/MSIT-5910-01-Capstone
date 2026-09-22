. "$PSScriptRoot/Common.ps1"
$secrets=Get-DemoSecrets
$headers=@{'X-Api-Key'=(Unprotect $secrets.ErpKey)}
$listener=Get-NetTCPConnection -LocalPort 5081 -State Listen -ErrorAction Stop | Select-Object -First 1
$records=@(Get-Content "$repo/.local/processes.json" -Raw|ConvertFrom-Json)
$record=$records|Where-Object Id -eq $listener.OwningProcess
$process=Get-Process -Id $listener.OwningProcess
if(-not $record -or $process.StartTime.ToUniversalTime().Ticks -ne ([datetime]$record.StartTime).ToUniversalTime().Ticks){throw 'WMS listener is not the recorded demo process'}
$id=[Guid]::NewGuid().ToString()
$body=@{version=1;orderId=$id;sku='TEST-UNAVAILABLE-WMS';quantity=1;correlationId=[Guid]::NewGuid().ToString()}|ConvertTo-Json
try{
    Stop-Process -Id $process.Id
    Write-Output 'Stopped the recorded WMS demo process to cause a real connection failure.'
    $null=Invoke-RestMethod http://127.0.0.1:5080/api/orders -Method Post -Headers $headers -ContentType application/json -Body $body
    for($i=0;$i -lt 40;$i++){
        $status=Invoke-RestMethod "http://127.0.0.1:5080/api/orders/$id" -Headers $headers
        if($status.status -ne 'Pending'){break};Start-Sleep -Milliseconds 500
    }
    if($status.status -ne 'RecoveryRequired' -or $null -ne $status.receipt){throw 'Uncertain delivery was not represented correctly'}
    Write-Output "PASS: order $id is RecoveryRequired with no claimed receipt."
}finally{
    & "$PSScriptRoot/Stop-Demo.ps1"
    & "$PSScriptRoot/Start-Demo.ps1" -NoBuild
}
$restored=Invoke-RestMethod "http://127.0.0.1:5080/api/orders/$id" -Headers $headers
if($restored.status -ne 'RecoveryRequired'){throw 'Failure state did not survive restart'}
Write-Output 'PASS: RecoveryRequired survives restart. Both services are healthy again.'
Write-Output 'Automatic requeue is intentionally deferred to Unit 5; the failed order remains available for inspection.'
