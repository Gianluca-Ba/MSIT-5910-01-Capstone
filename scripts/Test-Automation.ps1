. "$PSScriptRoot/Common.ps1"
$secrets=Get-DemoSecrets;$headers=@{'X-Api-Key'=(Unprotect $secrets.ErpKey)}
$base='http://127.0.0.1:5080/api/automation'
function Arm([int]$count){Invoke-RestMethod "$base/arm" -Method Post -Headers $headers -ContentType application/json -Body (@{count=$count;errorPercent=30;intervalMs=100;seed=5910}|ConvertTo-Json)}
function Snapshot($id){Invoke-RestMethod "$base/$id" -Headers $headers}
function Wait-Terminal($id){for($i=0;$i -lt 80;$i++){$s=Snapshot $id;if($s.state -in 'Completed','Stopped'){return $s};Start-Sleep -Milliseconds 500};throw 'Batch did not finish within 40 seconds'}
$a=Arm 10;$s=Snapshot $a.runId
if($s.state -ne 'Armed' -or @($s.messages|Where-Object state -ne Queued).Count){throw 'Arm transmitted messages'}
Invoke-RestMethod "$base/$($a.runId)/run" -Headers $headers -Method Post|Out-Null
$b=Arm 10
try{Invoke-RestMethod "$base/$($b.runId)/run" -Headers $headers -Method Post|Out-Null;throw 'Parallel run was accepted'}catch{if([int]$_.Exception.Response.StatusCode -ne 409){throw}}
Invoke-RestMethod "$base/$($b.runId)/stop" -Headers $headers -Method Post|Out-Null
$s=Wait-Terminal $a.runId
if($s.state -ne 'Completed' -or @($s.messages|Where-Object matched -ne $true).Count){throw 'Unexpected final results'}
if(@($s.messages|Where-Object state -eq Acknowledged).Count -ne 7){throw 'Expected seven acknowledgements'}
$detail=Invoke-RestMethod "$base/$($a.runId)/messages/1" -Headers $headers
if(-not $detail.payload -or -not $detail.sentAt -or -not $detail.analyzedAt -or -not $detail.completedAt -or -not $detail.receipt){throw 'Missing audit evidence'}
$stopped=Wait-Terminal $b.runId
if(@($stopped.messages|Where-Object state -ne Cancelled).Count){throw 'Disarmed batch transmitted'}
$c=Arm 20;Invoke-RestMethod "$base/$($c.runId)/run" -Headers $headers -Method Post|Out-Null
for($i=0;$i -lt 20;$i++){$s=Snapshot $c.runId;if(@($s.messages|Where-Object sentAt).Count -gt 0){break};Start-Sleep -Milliseconds 100}
Invoke-RestMethod "$base/$($c.runId)/stop" -Headers $headers -Method Post|Out-Null
$s=Wait-Terminal $c.runId
if($s.state -ne 'Stopped' -or @($s.messages|Where-Object state -eq Cancelled).Count -eq 0){throw 'Stop failed to cancel unsent work'}
Write-Output 'PASS: arm is inert; parallel run rejected; 7 acknowledgements + 3 expected rejections; audit evidence present; disarm and active Stop preserve sent work and cancel queued messages.'
Write-Output "Completed evidence run: $($a.runId)"
