. "$PSScriptRoot/Common.ps1"
Add-Type -AssemblyName System.Net.Http
$secrets=Get-DemoSecrets
$client=New-Object Net.Http.HttpClient
$client.Timeout=[TimeSpan]::FromSeconds(15)
$checks=0
function Assert-True([bool]$condition,[string]$name){if(-not $condition){throw "FAIL: $name"};$script:checks++;Write-Output "PASS: $name"}
function Request([string]$method,[string]$url,[string]$key,[string]$body){
    $req=New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::new($method),$url)
    if($key){$req.Headers.Add('X-Api-Key',$key)}
    if($body){$req.Content=New-Object Net.Http.StringContent($body,[Text.Encoding]::UTF8,'application/json')}
    try{$response=$client.SendAsync($req).GetAwaiter().GetResult();try{return @{Code=[int]$response.StatusCode;Body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}}finally{$response.Dispose()}}finally{$req.Dispose()}
}
function Count-Order([string]$role,[string]$table,[Guid]$id,[string]$source='demo'){
    $c=New-Object System.Data.SqlClient.SqlConnection((Unprotect $secrets["${role}Connection"]))
    try{$c.Open();$cmd=$c.CreateCommand();$cmd.CommandText="SELECT COUNT(*) FROM dbo.[$table] WHERE SourceId=@source AND OrderId=@id";[void]$cmd.Parameters.AddWithValue('@id',$id);[void]$cmd.Parameters.AddWithValue('@source',$source);return [int]$cmd.ExecuteScalar()}finally{$c.Dispose()}
}
$wms='http://127.0.0.1:5081/api/acceptances';$erp='http://127.0.0.1:5080/api/orders'
$wk=Unprotect $secrets.WmsKey;$ek=Unprotect $secrets.ErpKey;$other=Unprotect $secrets.OtherKey
try{
    $id=[Guid]::NewGuid();$o=@{version=1;orderId=$id.ToString();sku='TEST-PALLET';quantity=10;correlationId=[Guid]::NewGuid().ToString()};$json=$o|ConvertTo-Json -Compress
    Assert-True ((Request POST $wms '' $json).Code -eq 401) 'Unauthenticated WMS request rejected'
    Assert-True ((Count-Order Wms AcceptedOrder $id) -eq 0) 'Unauthenticated request has no effect'
    $bad=$o.Clone();$bad.quantity=0
    Assert-True ((Request POST $erp $ek ($bad|ConvertTo-Json)).Code -eq 400) 'Invalid quantity rejected at ERP'
    Assert-True ((Count-Order Erp SourceOrder $id) -eq 0) 'Invalid order not persisted'
    $spoof=$o.Clone();$spoof.sourceId='other'
    Assert-True ((Request POST $erp $ek ($spoof|ConvertTo-Json)).Code -eq 400) 'Payload cannot supply authenticated source'
    $large=$o.Clone();$large.sku='A'*5000
    Assert-True ((Request POST $wms $wk ($large|ConvertTo-Json)).Code -eq 413) 'Oversized body rejected'
    # Send ten genuinely concurrent HTTP requests to the real receiver.
    $tasks=@();$requests=@()
    for($i=0;$i -lt 10;$i++){
        $req=New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post,$wms)
        $req.Headers.Add('X-Api-Key',$wk);$req.Content=New-Object Net.Http.StringContent($json,[Text.Encoding]::UTF8,'application/json')
        $requests+=,$req;$tasks+=,$client.SendAsync($req)
    }
    $codes=@();$receipts=@()
    for($i=0;$i -lt $tasks.Count;$i++){
        $response=$tasks[$i].GetAwaiter().GetResult()
        try{$codes+=[int]$response.StatusCode;$receipts+=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json).receiptId}finally{$response.Dispose();$requests[$i].Dispose()}
    }
    Assert-True (@($codes|Where-Object{$_ -eq 201}).Count -eq 1) 'Concurrent acceptance creates exactly one order'
    Assert-True (@($codes|Where-Object{$_ -eq 200}).Count -eq 9) 'Nine concurrent duplicates return HTTP 200'
    Assert-True (@($receipts|Select-Object -Unique).Count -eq 1) 'Concurrent duplicates share original receipt'
    Assert-True ((Count-Order Wms AcceptedOrder $id) -eq 1) 'SQL confirms one WMS business effect'
    Assert-True ((Count-Order Wms AcceptanceReceipt $id) -eq 1) 'SQL confirms one WMS receipt'
    $o.correlationId=[Guid]::NewGuid().ToString()
    Assert-True ((Request POST $wms $wk ($o|ConvertTo-Json)).Code -eq 200) 'Changed tracing metadata remains a duplicate'
    $o.quantity=11
    Assert-True ((Request POST $wms $wk ($o|ConvertTo-Json)).Code -eq 409) 'Changed immutable content conflicts'
    $o.quantity=10
    Assert-True ((Request POST $wms $other ($o|ConvertTo-Json)).Code -eq 201) 'Separate authenticated source has separate identity'
    Assert-True ((Count-Order Wms AcceptedOrder $id 'other') -eq 1) 'SQL confirms isolated source identity'
    $erpId=[Guid]::NewGuid();$o.orderId=$erpId.ToString();$json=$o|ConvertTo-Json -Compress
    Assert-True ((Request POST $erp $ek $json).Code -eq 202) 'ERP returns durable submission acceptance'
    Assert-True ((Count-Order Erp SourceOrder $erpId) -eq 1) 'Source order persisted'
    Assert-True ((Count-Order Erp OutboxMessage $erpId) -eq 1) 'Outgoing intention persisted'
    Assert-True ((Request GET "$erp/$erpId" $other '').Code -eq 404) 'Cross-source status lookup denied'
    Assert-True ((Request GET "$erp/$erpId" '' '').Code -eq 401) 'Unauthenticated status lookup denied'
    for($i=0;$i -lt 30;$i++){$status=(Request GET "$erp/$erpId" $ek '').Body|ConvertFrom-Json;if($status.status -ne 'Pending'){break};Start-Sleep -Milliseconds 500}
    Assert-True ($status.status -eq 'Acknowledged') 'Worker delivers and stores acknowledgement'
    Assert-True ((Count-Order Erp DeliveryAttempt $erpId) -eq 1) 'Delivery attempt recorded'
    Assert-True ((Count-Order Wms AcceptedOrder $erpId) -eq 1) 'ERP workflow reaches real WMS store'
    Assert-True ((Request POST $erp $ek $json).Code -eq 202) 'ERP repeated submission is idempotent'
    Assert-True ((Count-Order Erp OutboxMessage $erpId) -eq 1) 'ERP duplicate creates no second outbox message'
    Write-Output "Passed $checks HTTP and SQL assertions. These are integration checks, separate from unit tests."
}finally{$client.Dispose()}
