param([ValidateRange(1,10)][int]$Trials=3,[ValidateRange(5,100)][int]$OrdersPerTrial=30,[string]$EvidenceDirectory)
. "$PSScriptRoot/Common.ps1"
Add-Type -AssemblyName System.Net.Http
$output=if($EvidenceDirectory){[IO.Path]::GetFullPath($EvidenceDirectory)}else{Join-Path $repo 'docs/unit6/evidence'}
[void](New-Item -ItemType Directory -Force $output)
$secrets=Get-DemoSecrets
$ek=Unprotect $secrets.ErpKey; $wk=Unprotect $secrets.WmsKey
$client=New-Object Net.Http.HttpClient
$client.Timeout=[TimeSpan]::FromSeconds(30)
$rows=[Collections.Generic.List[object]]::new()
$trialsOut=[Collections.Generic.List[object]]::new()
$custom=[Collections.Generic.List[object]]::new()
function Request($method,$url,$key,$body) {
    $req=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method),$url)
    $req.Headers.Add('X-Api-Key',$key)
    if($null -ne $body){$req.Content=[Net.Http.StringContent]::new(($body|ConvertTo-Json -Depth 20 -Compress),[Text.Encoding]::UTF8,'application/json')}
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try {
        $res=$client.SendAsync($req).GetAwaiter().GetResult()
        try {$raw=$res.Content.ReadAsStringAsync().GetAwaiter().GetResult();$watch.Stop();return @{Code=[int]$res.StatusCode;Ms=$watch.Elapsed.TotalMilliseconds;Raw=$raw;Data=if($raw){$raw|ConvertFrom-Json}else{$null}}}
        finally {$res.Dispose()}
    } finally {$req.Dispose()}
}
function SqlCount($role,$table,$id) {
    $c=[System.Data.SqlClient.SqlConnection]::new((Unprotect $secrets["${role}Connection"]))
    try {$c.Open();$cmd=$c.CreateCommand();$cmd.CommandText="SELECT COUNT(*) FROM dbo.[$table] WHERE SourceId='demo' AND OrderId=@id";[void]$cmd.Parameters.AddWithValue('@id',[Guid]$id);return [int]$cmd.ExecuteScalar()} finally {$c.Dispose()}
}
function NewOrder($sku) {return @{version=1;orderId=[Guid]::NewGuid().ToString();sku=$sku;quantity=10;correlationId=[Guid]::NewGuid().ToString()}}
$erp='http://127.0.0.1:5080';$wms='http://127.0.0.1:5081'
$started=[DateTime]::UtcNow.ToString('o')
try {
    # Fail before adding a workload if another sender/queued workload is active.
    $c=[System.Data.SqlClient.SqlConnection]::new((Unprotect $secrets.ErpConnection))
    try {$c.Open();$cmd=$c.CreateCommand();$cmd.CommandText="SELECT (SELECT COUNT(*) FROM dbo.OutboxMessage WHERE Status='Pending')+(SELECT COUNT(*) FROM dbo.AutomationRun WHERE State IN ('Running','Draining','Stopping'))";if([int]$cmd.ExecuteScalar() -ne 0){throw 'Wait for existing workload to finish before measuring.'}}finally{$c.Dispose()}
    foreach($port in @(5080,5081)){if((Invoke-RestMethod "http://127.0.0.1:$port/health").status -ne 'ready'){throw 'Service not ready'}}
    # Three warm-up orders, retained in SQL but excluded from all measured samples.
    for($i=1;$i -le 3;$i++){
        $o=NewOrder 'U6-WARMUP';$r=Request POST "$erp/api/orders" $ek $o
        if($r.Code -ne 202){throw 'Warm-up submission failed'}
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do {$s=Request GET "$erp/api/orders/$($o.orderId)" $ek $null;if($s.Data.status -ne 'Pending'){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
        if($s.Data.status -ne 'Acknowledged'){throw 'Warm-up did not acknowledge'}
    }
    for($trial=1;$trial -le $Trials;$trial++) {
        $batch=[Collections.Generic.List[object]]::new();$clock=[Diagnostics.Stopwatch]::StartNew()
        for($i=1;$i -le $OrdersPerTrial;$i++){
            $o=NewOrder "U6-T$trial-$i";$startMs=$clock.Elapsed.TotalMilliseconds
            $r=Request POST "$erp/api/orders" $ek $o
            $row=[pscustomobject]@{Trial=$trial;Sequence=$i;Scenario='valid';OrderId=$o.orderId;ExpectedHttp=202;Http=$r.Code;HttpMs=$r.Ms;SubmitOffsetMs=$startMs;ObservedEndToEndMs=$null;State='Unobserved';ReceiptId=$null;WmsOrders=$null;WmsReceipts=$null;Matched=$false;Payload=($o|ConvertTo-Json -Compress)}
            $rows.Add($row);$batch.Add($row)
        }
        $deadline=[DateTime]::UtcNow.AddSeconds(180)
        do {
            foreach($row in $batch){
                if($row.State -notin @('Unobserved','Pending')){continue}
                $r=Request GET "$erp/api/orders/$($row.OrderId)" $ek $null
                if($r.Code -eq 200){
                    $row.State=$r.Data.status
                    if($row.State -ne 'Pending'){$row.ObservedEndToEndMs=$clock.Elapsed.TotalMilliseconds-$row.SubmitOffsetMs;$row.ReceiptId=$r.Data.receipt.receiptId}
                }
            }
            if(@($batch|Where-Object {$_.State -in @('Unobserved','Pending')}).Count -eq 0){break}
            Start-Sleep -Milliseconds 100
        }while([DateTime]::UtcNow -lt $deadline)
        $clock.Stop()
        $acked=@($batch|Where-Object {$_.State -eq 'Acknowledged'}).Count
        $trialsOut.Add([pscustomobject]@{Trial=$trial;Submitted=$OrdersPerTrial;Acknowledged=$acked;ObservationSeconds=$clock.Elapsed.TotalSeconds;AcknowledgedPerSecond=$acked/$clock.Elapsed.TotalSeconds})
        foreach($row in $batch){
            $row.WmsOrders=SqlCount Wms AcceptedOrder $row.OrderId;$row.WmsReceipts=SqlCount Wms AcceptanceReceipt $row.OrderId
            $row.Matched=$row.Http -eq 202 -and $row.State -eq 'Acknowledged' -and $row.WmsOrders -eq 1 -and $row.WmsReceipts -eq 1 -and [bool]$row.ReceiptId
        }
        # Ten independent examples per scenario, outside the timed delivery window.
        for($i=0;$i -lt [Math]::Min(10,$OrdersPerTrial);$i++){
            $original=$batch[$i];$o=$original.Payload|ConvertFrom-Json
            foreach($scenario in @('duplicate','conflict','invalid')){
                $copy=$original.Payload|ConvertFrom-Json;$copy.correlationId=[Guid]::NewGuid().ToString()
                if($scenario -eq 'conflict'){$copy.quantity=11}
                if($scenario -eq 'invalid'){$copy.orderId=[Guid]::NewGuid().ToString();$copy.quantity=0}
                $url=if($scenario -eq 'invalid'){"$erp/api/orders"}else{"$wms/api/acceptances"}
                $key=if($scenario -eq 'invalid'){$ek}else{$wk}
                $expected=if($scenario -eq 'duplicate'){200}elseif($scenario -eq 'conflict'){409}else{400}
                $r=Request POST $url $key $copy
                $count=SqlCount Wms AcceptedOrder $copy.orderId;$receiptCount=SqlCount Wms AcceptanceReceipt $copy.orderId
                $correct=$r.Code -eq $expected
                if($scenario -eq 'invalid'){$correct=$correct -and $count -eq 0 -and (SqlCount Erp SourceOrder $copy.orderId) -eq 0}
                else {$correct=$correct -and $count -eq 1 -and $receiptCount -eq 1}
                if($scenario -eq 'duplicate'){$correct=$correct -and $r.Data.receiptId -eq $original.ReceiptId}
                $rows.Add([pscustomobject]@{Trial=$trial;Sequence=$i+1;Scenario=$scenario;OrderId=$copy.orderId;ExpectedHttp=$expected;Http=$r.Code;HttpMs=$r.Ms;SubmitOffsetMs=$null;ObservedEndToEndMs=$null;State='HTTP observed';ReceiptId=$r.Data.receiptId;WmsOrders=$count;WmsReceipts=$receiptCount;Matched=[bool]$correct;Payload=($copy|ConvertTo-Json -Compress)})
            }
        }
        Write-Output "Trial $trial completed: $acked/$OrdersPerTrial acknowledged in $([Math]::Round($clock.Elapsed.TotalSeconds,2)) s"
    }
    $name='U6_'+[Guid]::NewGuid().ToString('N')
    $definition=@{name=$name;fields=@(@{section='Header';name='MessageId';type='identifier';required=$true},@{section='Destination';name='Area';type='choice';required=$true;choices=@('Inbound','Storage','Outbound')},@{section='Details';name='Quantity';type='integer';required=$true;min=1;max=100})}
    $saved=Request POST "$erp/api/message-types" $ek $definition
    if($saved.Code -ne 200){throw 'Definition save failed'}
    # Warm up custom generation, excluded from the three measured calls.
    $null=Request POST "$erp/api/message-types/$name/1/generate" $ek @{count=10;errorPercent=30;seed=6000}
    foreach($seed in @(6001,6002,6003)){
        $r=Request POST "$erp/api/message-types/$name/1/generate" $ek @{count=1000;errorPercent=30;seed=$seed}
        $custom.Add([pscustomobject]@{Seed=$seed;BatchId=$r.Data.batchId;Definition=$name;Http=$r.Code;HttpMs=$r.Ms;Count=@($r.Data.messages).Count;Valid=@($r.Data.messages|Where-Object {$_.actualValid}).Count;Invalid=@($r.Data.messages|Where-Object {-not $_.actualValid}).Count;Matched=@($r.Data.messages|Where-Object {$_.matched}).Count;MessagesPerSecond=@($r.Data.messages).Count/($r.Ms/1000)})
    }
} finally {
    $rows|Export-Csv "$output/requests.csv" -NoTypeInformation -Encoding UTF8
    $trialsOut|Export-Csv "$output/trials.csv" -NoTypeInformation -Encoding UTF8
    $custom|Export-Csv "$output/custom-validation.csv" -NoTypeInformation -Encoding UTF8
    @{StartedUtc=$started;FinishedUtc=[DateTime]::UtcNow.ToString('o');Trials=$Trials;OrdersPerTrial=$OrdersPerTrial;PollDelayMs=100;WarmupOrders=3;Platform=[Environment]::OSVersion.VersionString;ProcessorArchitecture=$env:PROCESSOR_ARCHITECTURE;Harness='PowerShell HttpClient with Stopwatch; SQL assertions after timed delivery window';TimeoutSeconds=180}|ConvertTo-Json|Set-Content "$output/method.json" -Encoding UTF8
    $client.Dispose();$ek=$null;$wk=$null
}
$failures=@($rows|Where-Object {-not $_.Matched}).Count
Write-Output "Measured $($rows.Count) order scenarios; mismatches: $failures. Custom validation batches: $($custom.Count)."
if($failures -gt 0 -or @($custom|Where-Object {$_.Http -ne 200 -or $_.Matched -ne 1000 -or $_.Invalid -ne 300}).Count -gt 0){throw 'Evaluation contains mismatches; inspect raw evidence.'}
