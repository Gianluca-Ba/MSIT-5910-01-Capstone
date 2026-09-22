. "$PSScriptRoot/Common.ps1"
$state=Join-Path $repo '.local/processes.json'
if(Test-Path $state){
    foreach($entry in @(Get-Content $state -Raw|ConvertFrom-Json)){
        $process=Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
        if($process -and $process.StartTime.ToUniversalTime().ToString('o') -eq $entry.StartTime){Stop-Process -Id $entry.Id;Write-Output "Stopped demo process $($entry.Id)"}
    }
    Remove-Item -LiteralPath $state
}
