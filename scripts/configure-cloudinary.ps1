param([switch]$SecretOnly)
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot '../backend/MyTaigaAPI/MyTaigaAPI.csproj'
if(-not $SecretOnly){
 $cloudName=Read-Host 'Cloudinary Cloud name (trong Console)'
 if($cloudName -notmatch '^[a-zA-Z0-9_-]{1,100}$'){throw 'Cloud name không hợp lệ.'}
 $apiKey=Read-Host 'Cloudinary API Key'
 if([string]::IsNullOrWhiteSpace($apiKey)){throw 'Thiếu API Key.'}
}
try{
 do{
  $secret=Read-Host 'Dán API Secret THẬT từ Cloudinary (không nhập các dấu sao)' -AsSecureString
  $plain=[System.Net.NetworkCredential]::new('', $secret).Password
  $invalid=[string]::IsNullOrWhiteSpace($plain) -or $plain -match '^[*●•\s]+$' -or $plain -match '^(<.*>|your_api_secret|api_secret)$'
  if($invalid){Write-Host 'Đây là chuỗi che/giá trị mẫu, chưa phải API Secret thật. Hãy dùng nút Copy của API Secret trên Cloudinary.' -ForegroundColor Yellow}
 }while($invalid)
 $values=@{'Cloudinary:ApiSecret'=$plain}
 if(-not $SecretOnly){$values['Cloudinary:CloudName']=$cloudName;$values['Cloudinary:ApiKey']=$apiKey}
 # Windows PowerShell may otherwise prepend a UTF-8 BOM to native stdin.
 $previousOutputEncoding=$OutputEncoding
 try{
  $OutputEncoding=New-Object System.Text.UTF8Encoding($false)
  $values | ConvertTo-Json -Compress | dotnet user-secrets set --project $project
  if($LASTEXITCODE -ne 0){throw 'Không lưu được cấu hình.'}
 }
 finally{$OutputEncoding=$previousOutputEncoding}
 Write-Host 'Đã lưu cấu hình riêng trên máy. Dừng API bằng Ctrl+C rồi chạy lại để dùng cấu hình mới.'
}
finally{Remove-Variable plain,secret,apiKey,values -ErrorAction SilentlyContinue}
