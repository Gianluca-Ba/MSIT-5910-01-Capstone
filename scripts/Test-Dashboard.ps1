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
    try{$response=$client.SendAsync($req).GetAwaiter().GetResult();try{return @{Code=[int]$response.StatusCode;Body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();Headers=$response.Headers.ToString()}}finally{$response.Dispose()}}finally{$req.Dispose()}
}
$root='http://127.0.0.1:5080';$wms='http://127.0.0.1:5081'
$ek=Unprotect $secrets.ErpKey;$wk=Unprotect $secrets.WmsKey;$other=Unprotect $secrets.OtherKey
try {
    $page=Request GET "$root/" '' ''
    Assert-True ($page.Code -eq 200 -and $page.Body.Contains('Every order. One outcome.')) 'Dashboard loads without exposing business data'
    Assert-True ($page.Headers.Contains("frame-ancestors 'none'") -and $page.Headers.Contains('no-store')) 'Dashboard sends framing and cache protections'
    Assert-True (-not $page.Body.Contains($ek) -and -not $page.Body.Contains($wk)) 'HTML contains no service API keys'
    foreach($asset in @('dashboard.css','dashboard.js')){Assert-True ((Request GET "$root/$asset" '' '').Code -eq 200) "$asset is served"}
    Assert-True ((Request GET "$root/settings.xml" '' '').Code -eq 401) 'Non-dashboard paths remain authenticated'
    Assert-True ((Request GET "$wms/" '' '').Code -eq 401) 'WMS does not expose dashboard paths'
    $id=[Guid]::NewGuid().ToString()
    $order=@{version=1;orderId=$id;sku='DASHBOARD-TEST';quantity=7;correlationId=[Guid]::NewGuid().ToString()}
    $json=$order|ConvertTo-Json -Compress
    Assert-True ((Request GET "$root/api/verification/$id" '' '').Code -eq 401) 'Evidence requires authentication'
    Assert-True ((Request POST "$root/api/verification/acceptances" '' $json).Code -eq 401) 'Receiver checks require authentication'
    Assert-True ((Request GET "$root/api/verification/$id" $ek '').Code -eq 404) 'Unknown evidence returns 404'
    Assert-True ((Request POST "$root/api/orders" $ek $json).Code -eq 202) 'Dashboard order accepted by ERP'
    for($i=0;$i -lt 30;$i++){
        $status=(Request GET "$root/api/orders/$id" $ek '').Body|ConvertFrom-Json
        if($status.status -ne 'Pending'){break};Start-Sleep -Milliseconds 500
    }
    Assert-True ($status.status -eq 'Acknowledged') 'Dashboard order reaches Acknowledged'
    $duplicate=Request POST "$root/api/verification/acceptances" $ek $json
    Assert-True ($duplicate.Code -eq 200 -and ($duplicate.Body|ConvertFrom-Json).receiptId -eq $status.receipt.receiptId) 'Gateway duplicate preserves original WMS receipt'
    $order.quantity=8
    Assert-True ((Request POST "$root/api/verification/acceptances" $ek ($order|ConvertTo-Json)).Code -eq 409) 'Gateway preserves WMS conflict response'
    $order.quantity=0
    Assert-True ((Request POST "$root/api/verification/acceptances" $ek ($order|ConvertTo-Json)).Code -eq 400) 'Gateway preserves backend validation'
    $evidence=Request GET "$root/api/verification/$id" $ek ''
    $data=$evidence.Body|ConvertFrom-Json
    Assert-True ($evidence.Code -eq 200 -and $data.order.quantity -eq 7 -and $data.orderCount -eq 1 -and $data.receiptCount -eq 1) 'SQL-backed evidence confirms unchanged order and one receipt'
    Assert-True ($data.receipt.receiptId -eq $status.receipt.receiptId) 'ERP and WMS receipts match'
    Assert-True ((Request GET "$wms/api/acceptances/$id" '' '').Code -eq 401) 'WMS evidence requires authentication'
    Assert-True ((Request GET "$wms/api/acceptances/$id" $other '').Code -eq 404) 'WMS evidence is source-isolated'
    Assert-True ((Request GET "$root/api/verification/$id" $other '').Code -eq 503) 'Unmapped ERP source cannot borrow demo WMS identity'
    Write-Output "$checks dashboard integration assertions passed. Test records retained."
} finally { $client.Dispose() }
