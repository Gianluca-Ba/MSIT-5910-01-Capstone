param([PSCredential]$AdminCredential=(Get-Credential -Message 'SQL administrator for the additive message customization migration'))
. "$PSScriptRoot/Common.ps1"
$secrets=Get-DemoSecrets
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder((Unprotect $secrets.ErpConnection))
$builder['User ID']=$AdminCredential.UserName
$builder['Password']=$AdminCredential.GetNetworkCredential().Password
$connection=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
try {
 $connection.Open()
 $command=$connection.CreateCommand();$command.CommandTimeout=60
 $command.CommandText=Get-Content "$repo/database/erp-customization.sql" -Raw
 [void]$command.ExecuteNonQuery()
 Write-Output 'Message customization migration complete. Existing order data retained.'
} finally {$connection.Dispose()}
