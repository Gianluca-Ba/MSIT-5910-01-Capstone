param([Parameter(Mandatory)][string]$Server,[PSCredential]$AdminCredential=(Get-Credential -Message 'SQL setup login'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$local=Join-Path $repo '.local'
if(Test-Path "$local/secrets.xml"){throw 'Local configuration already exists. Initialization is only for a fresh demo.'}
function New-Key {
    $bytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try{$rng.GetBytes($bytes);return [Convert]::ToBase64String($bytes)}finally{$rng.Dispose()}
}
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source']=$Server
$builder['Initial Catalog']='master'
$builder['User ID']=$AdminCredential.UserName
$builder['Password']=$AdminCredential.GetNetworkCredential().Password
$builder['Encrypt']=$true
$builder['TrustServerCertificate']=$true
$builder['Connect Timeout']=10
$c=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
$c.Open()
try {
    foreach($db in @('CapstoneErp','CapstoneWms')) {
        $cmd=$c.CreateCommand();$cmd.CommandText='SELECT DB_ID(@db)';[void]$cmd.Parameters.AddWithValue('@db',$db)
        if($cmd.ExecuteScalar() -isnot [DBNull]){throw "Database $db already exists. Refusing to modify an existing database."}
    }
    foreach($login in @('CapstoneErpApp','CapstoneWmsApp')) {
        $cmd=$c.CreateCommand();$cmd.CommandText='SELECT SUSER_ID(@login)';[void]$cmd.Parameters.AddWithValue('@login',$login)
        if($cmd.ExecuteScalar() -isnot [DBNull]){throw "Login $login already exists. Refusing to modify it."}
    }
    $secrets=@{}
    foreach($role in @('Erp','Wms')) {
        $db="Capstone$role";$login="Capstone${role}App";$password=(New-Key)+'aA1!'
        $cmd=$c.CreateCommand();$cmd.CommandText="CREATE DATABASE [$db]";$cmd.ExecuteNonQuery()|Out-Null
        $cmd.CommandText="CREATE LOGIN [$login] WITH PASSWORD=N'$password', CHECK_POLICY=ON";$cmd.ExecuteNonQuery()|Out-Null
        $c.ChangeDatabase($db)
        $cmd.CommandText=Get-Content (Join-Path $repo "database/$($role.ToLower()).sql") -Raw
        $cmd.ExecuteNonQuery()|Out-Null
        $cmd.CommandText="CREATE USER [$login] FOR LOGIN [$login]; GRANT SELECT ON dbo.SchemaVersion TO [$login];"
        $cmd.ExecuteNonQuery()|Out-Null
        $tables=if($role -eq 'Erp'){@('SourceOrder','OutboxMessage','DeliveryAttempt')}else{@('AcceptedOrder','AcceptanceReceipt')}
        foreach($table in $tables){$cmd.CommandText="GRANT SELECT,INSERT ON dbo.[$table] TO [$login]";$cmd.ExecuteNonQuery()|Out-Null}
        if($role -eq 'Erp'){$cmd.CommandText="GRANT UPDATE ON dbo.OutboxMessage TO [$login]";$cmd.ExecuteNonQuery()|Out-Null}
        $appBuilder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($builder.ConnectionString)
        $appBuilder['Initial Catalog']=$db;$appBuilder['User ID']=$login;$appBuilder['Password']=$password
        $secrets["${role}Connection"]=ConvertTo-SecureString $appBuilder.ConnectionString -AsPlainText -Force
        $secrets["${role}Key"]=ConvertTo-SecureString (New-Key) -AsPlainText -Force
        $c.ChangeDatabase('master')
        Write-Output "Created $db with dedicated application login."
    }
    $secrets['OtherKey']=ConvertTo-SecureString (New-Key) -AsPlainText -Force
    New-Item -ItemType Directory -Force $local|Out-Null
    $secrets|Export-Clixml "$local/secrets.xml"
    Write-Output 'Saved user-bound Windows encrypted configuration under .local; administrator credentials were not saved.'
}finally{$c.Dispose()}
