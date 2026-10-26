param(
    [ValidatePattern('^MyTaiga_\w*Checks_\w+$')][string]$TestDatabase='MyTaiga_ProjectChecks_20261025',
    [string]$SqlServer='.\SQLEXPRESS',
    [ValidateRange(1024,65535)][int]$Port=5159
)
# Test-only process settings; never save these values into real User Secrets.
$env:ConnectionStrings__ScrumbanDb="Server=$SqlServer;Database=$TestDatabase;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
$env:Jwt__Key='integration-test-only-key-not-for-production-20261025'
$env:Jwt__Issuer='MyTaigaIntegration'
$env:Jwt__Audience='MyTaigaIntegration'
$env:Email__Provider='Development'
$env:Storage__TaskFilesRoot="$PSScriptRoot/../backend/MyTaigaAPI/App_Data/test-task-files"
$env:Frontend__ResetPasswordUrl='http://localhost:5173/reset-password'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__LogLevel__Default='Critical'
dotnet run --no-build --no-launch-profile --project "$PSScriptRoot/../backend/MyTaigaAPI/MyTaigaAPI.csproj" --urls "http://127.0.0.1:$Port"
