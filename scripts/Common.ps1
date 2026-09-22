$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
function Get-Dotnet {
    $command=Get-Command dotnet -ErrorAction SilentlyContinue
    if($command){return $command.Source}
    $portable=Join-Path (Split-Path $repo) 'tools/dotnet/dotnet.exe'
    if(Test-Path $portable){return $portable}
    throw 'Install the SDK pinned in global.json, or place it in ../tools/dotnet.'
}
function Get-DemoSecrets { Import-Clixml (Join-Path $repo '.local/secrets.xml') }
function Unprotect([Security.SecureString]$value){(New-Object PSCredential('local',$value)).GetNetworkCredential().Password}
