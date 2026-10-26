param(
 [ValidatePattern('^MyTaiga_\w*Checks_\w+$')][string]$Database='MyTaiga_InstallChecks_20261026',
 [string]$SqlServer='.\SQLEXPRESS'
)
$ErrorActionPreference='Stop'
$exists=sqlcmd -S $SqlServer -E -C -I -b -h -1 -W -Q "SET NOCOUNT ON;SELECT COUNT(*) FROM sys.databases WHERE name='$Database'"
if($LASTEXITCODE -ne 0){throw 'Cannot connect to SQL Server.'}
if($exists.Trim() -ne '0'){throw 'Test database already exists; refusing to overwrite.'}
$folder=Join-Path $env:TEMP ('mytaiga-install-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder | Out-Null
foreach($file in Get-ChildItem "$PSScriptRoot/../Database/*.sql" | Sort-Object Name){
 $copy=Join-Path $folder $file.Name
 (Get-Content $file.FullName -Raw).Replace('ScrumbanDB',$Database) | Set-Content $copy -Encoding utf8
 sqlcmd -S $SqlServer -E -C -I -b -i $copy *> (Join-Path $folder ($file.Name+'.txt'))
 if($LASTEXITCODE -ne 0){throw "Failed $($file.Name); inspect $folder"}
 Write-Output "Installed $($file.Name)"
}
Write-Output "Created disposable database $Database; logs: $folder"
