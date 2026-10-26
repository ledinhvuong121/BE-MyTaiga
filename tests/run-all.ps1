param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Set-Location $root
$names=@('projects','members','departments','epics','backlog','sprints','tasks','board','wip','flow','bugs','comments','attachments','notifications','chat','admin','auth')
$results=@()
foreach($name in $names){
 $log=Join-Path $env:TEMP "mytaiga-final-$name.log"
 python "tests/${name}_integration.py" *> $log
 if($LASTEXITCODE -ne 0){Get-Content $log -Tail 35;throw "Failed suite: $name"}
 $tail=Get-Content $log -Tail 1
 $results+=@{suite=$name;result=$tail}
 Write-Output "$name : $tail"
}
$results | ConvertTo-Json | Set-Content docs/test-results-2026-10-26.json -Encoding utf8
