param([PSCredential]$AdminCredential=(Get-Credential -Message 'SQL administrator for the additive ERP automation migration'))
. "$PSScriptRoot/Common.ps1"
$secrets=Get-DemoSecrets
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder((Unprotect $secrets.ErpConnection))
$builder['User ID']=$AdminCredential.UserName
$builder['Password']=$AdminCredential.GetNetworkCredential().Password
$connection=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
try {
 $connection.Open()
 $command=$connection.CreateCommand();$command.CommandTimeout=60
 $command.CommandText=Get-Content "$repo/database/erp-automation.sql" -Raw
 [void]$command.ExecuteNonQuery()
 $command.CommandText='SELECT COUNT(*) FROM dbo.MessageTemplate'
 Write-Output "Automation migration complete: $($command.ExecuteScalar()) templates. Existing order data retained."
} finally {$connection.Dispose()}
