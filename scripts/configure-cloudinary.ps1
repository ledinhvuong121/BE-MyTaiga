param()
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot '../backend/MyTaigaAPI/MyTaigaAPI.csproj'
$cloudName=Read-Host 'Cloudinary Cloud name (trong Console)'
if($cloudName -notmatch '^[a-zA-Z0-9_-]{1,100}$'){throw 'Cloud name không hợp lệ.'}
$apiKey=Read-Host 'Cloudinary API Key'
$secret=Read-Host 'Cloudinary API Secret (nhập kín, không gửi vào chat)' -AsSecureString
try{
 $plain=[System.Net.NetworkCredential]::new('', $secret).Password
 if([string]::IsNullOrWhiteSpace($apiKey) -or [string]::IsNullOrWhiteSpace($plain)){throw 'Thiếu API Key hoặc Secret.'}
 foreach($pair in @(@('Cloudinary:CloudName',$cloudName),@('Cloudinary:ApiKey',$apiKey),@('Cloudinary:ApiSecret',$plain))){
  dotnet user-secrets set $pair[0] $pair[1] --project $project
  if($LASTEXITCODE -ne 0){throw 'Không lưu được cấu hình.'}
 }
 Write-Host 'Đã lưu cấu hình riêng trên máy. Dừng API bằng Ctrl+C rồi chạy lại để dùng cấu hình mới.'
}
finally{Remove-Variable plain,secret,apiKey -ErrorAction SilentlyContinue}
