param([switch]$NoBrowser)
. "$PSScriptRoot/Common.ps1"
$health = Invoke-RestMethod 'http://127.0.0.1:5080/health' -TimeoutSec 5
if ($health.status -ne 'ready') { throw 'Start the demo services before opening the dashboard.' }
$secrets = Get-DemoSecrets
Set-Clipboard -Value (Unprotect $secrets.ErpKey)
Write-Output 'ERP API key copied to clipboard. Paste it in the dashboard and click Connect. Clear the clipboard after pasting. No key is stored in browser storage or a URL.'
if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:5080/' }
