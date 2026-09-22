param([switch]$NoBuild,[switch]$WorkerDisabled)
. "$PSScriptRoot/Common.ps1"
$dotnet=Get-Dotnet
if(-not $NoBuild){& $dotnet build $repo -c Release;if($LASTEXITCODE){throw 'Build failed'}}
foreach($port in @(5080,5081)){if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw "Port $port is already in use. Stop the existing demo first."}}
$secrets=Get-DemoSecrets
$records=@()
try {
    foreach($role in @('Wms','Erp')) {
        $env:ConnectionStrings__Database=Unprotect $secrets["${role}Connection"]
        $env:Auth__Clients__demo=Unprotect $secrets["${role}Key"]
        $env:Auth__Clients__other=Unprotect $secrets.OtherKey
        $env:Delivery__Keys__demo=Unprotect $secrets.WmsKey
        $env:Delivery__Enabled=if($WorkerDisabled){'false'}else{'true'}
        $env:DOTNET_ENVIRONMENT='Production'
        $dir=Join-Path $repo "src/Capstone.$role"
        $dll=Join-Path $dir "bin/Release/net10.0/Capstone.$role.dll"
        $process=Start-Process -FilePath $dotnet -ArgumentList ('"'+$dll+'"') -WorkingDirectory $dir -WindowStyle Hidden -PassThru -RedirectStandardOutput "$repo/.local/$role.log" -RedirectStandardError "$repo/.local/$role.error.log"
        $records+=@{Id=$process.Id;StartTime=$process.StartTime.ToUniversalTime().ToString('o')}
        $records|ConvertTo-Json|Set-Content "$repo/.local/processes.json"
        $port=if($role -eq 'Erp'){5080}else{5081};$ready=$false
        for($i=0;$i -lt 30;$i++){
            try{$health=Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 3;if($health.status -eq 'ready'){$ready=$true;break}}catch{}
            if($process.HasExited){throw "$role exited. Check .local/$role.error.log"}
            Start-Sleep -Milliseconds 500
        }
        if(-not $ready){throw "$role did not become ready; inspect .local logs."}
        Write-Output "$role ready at http://127.0.0.1:$port"
    }
}catch{& "$PSScriptRoot/Stop-Demo.ps1";throw}
finally {
    foreach($key in @('ConnectionStrings__Database','Auth__Clients__demo','Auth__Clients__other','Delivery__Keys__demo','Delivery__Enabled','DOTNET_ENVIRONMENT')){[Environment]::SetEnvironmentVariable($key,$null,'Process')}
}
