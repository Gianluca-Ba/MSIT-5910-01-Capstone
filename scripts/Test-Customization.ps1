. "$PSScriptRoot/Common.ps1"
Add-Type -AssemblyName System.Net.Http
$secrets=Get-DemoSecrets
$client=New-Object Net.Http.HttpClient
$client.Timeout=[TimeSpan]::FromSeconds(60)
$checks=0
function Assert-True([bool]$condition,[string]$name){if(-not $condition){throw "FAIL: $name"};$script:checks++;Write-Output "PASS: $name"}
function Request([string]$method,[string]$path,[string]$key,$body){
 $req=New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::new($method),"http://127.0.0.1:5080$path")
 if($key){$req.Headers.Add('X-Api-Key',$key)}
 if($null -ne $body){$req.Content=New-Object Net.Http.StringContent(($body|ConvertTo-Json -Depth 20 -Compress),[Text.Encoding]::UTF8,'application/json')}
 try{$response=$client.SendAsync($req).GetAwaiter().GetResult();try{$raw=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();$data=if($raw){$raw|ConvertFrom-Json}else{$null};return @{Code=[int]$response.StatusCode;Data=$data}}finally{$response.Dispose()}}finally{$req.Dispose()}
}
$key=Unprotect $secrets.ErpKey;$other=Unprotect $secrets.OtherKey
try {
 Assert-True ((Request GET '/api/message-types' '' $null).Code -eq 401) 'Definitions require authentication'
 $definition=@{name='Test_'+[Guid]::NewGuid().ToString('N');fields=@(@{section='Details';name='Quantity';type='integer';required=$true;min=1;max=10})}
 $v1=Request POST '/api/message-types' $key $definition
 Assert-True ($v1.Code -eq 200 -and $v1.Data.version -eq 1) 'Save first immutable version'
 $name=$definition.name
 $definition.fields[0].max=5
 $v2=Request POST '/api/message-types' $key $definition
 Assert-True ($v2.Code -eq 200 -and $v2.Data.version -eq 2) 'Editing creates version two'
 $valid=Request POST "/api/message-types/$name/1/validate" $key @{Details=@{Quantity=8}}
 $invalid=Request POST "/api/message-types/$name/2/validate" $key @{Details=@{Quantity=8}}
 Assert-True ($valid.Data.messages[0].actualValid -eq $true) 'Original version keeps its original range'
 Assert-True ($invalid.Data.messages[0].actualValid -eq $false) 'New version applies changed range'
 Assert-True ($invalid.Data.messages[0].issues[0].path -eq 'Details.Quantity') 'Rejection identifies field'
 $batch=Request POST "/api/message-types/$name/2/generate" $key @{count=1000;errorPercent=30;seed=5910}
 Assert-True ($batch.Code -eq 200 -and $batch.Data.messages.Count -eq 1000) 'Generate and record 1000 custom messages'
 Assert-True (@($batch.Data.messages|Where-Object {-not $_.actualValid}).Count -eq 300) 'Exactly 300 controlled validation rejections'
 Assert-True (@($batch.Data.messages|Where-Object {-not $_.matched}).Count -eq 0) 'Actual outcomes match expected outcomes'
 $id=$batch.Data.batchId
 Assert-True ((Request GET "/api/custom-batches/$id" $key $null).Data.messages.Count -eq 1000) 'Batch content and results read back from SQL'
 Assert-True ((Request GET "/api/custom-batches/$id" $other $null).Code -eq 404) 'Other source cannot read batch'
 Assert-True ((Request POST "/api/message-types/$name/2/generate" $other @{count=1;errorPercent=0;seed=1}).Code -eq 404) 'Other source cannot use definition'
 Assert-True ((Request POST "/api/message-types/$name/2/generate" $key @{count=1001;errorPercent=30;seed=1}).Code -eq 400) 'Oversized batch is rejected'
 $definition.fields[0].type='script'
 Assert-True ((Request POST '/api/message-types' $key $definition).Code -eq 400) 'Executable field type is rejected'
 Assert-True (@((Request GET '/api/custom-batches' $key $null).Data|Where-Object {$_.batchId -eq $id}).Count -eq 1) 'Batch appears in source history'
 Write-Output "$checks customization checks passed."
}finally{$client.Dispose();$key=$null;$other=$null}
