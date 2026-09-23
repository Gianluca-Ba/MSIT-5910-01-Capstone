. "$PSScriptRoot/Common.ps1"
$state=Join-Path $repo '.local/processes.json'
if(Test-Path $state){
    $entries=Get-Content $state -Raw|ConvertFrom-Json
    foreach($entry in $entries){
        $process=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
        if($process -and $process.StartTime.ToUniversalTime().Ticks -eq ([datetime]$entry.StartTime).ToUniversalTime().Ticks){Stop-Process -Id $entry.Id;Write-Output "Stopped demo process $($entry.Id)"}
    }
    Remove-Item -LiteralPath $state
}
