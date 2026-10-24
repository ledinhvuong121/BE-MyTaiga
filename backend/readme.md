# Backend MyTaiga

## Phạm vi hiện tại

Backend ASP.NET Core Web API, Entity Framework Core và SQL Server.

Đã triển khai:
- POST /api/v1/auth/login: đăng nhập và cấp JWT.
- POST /api/v1/auth/logout: đăng xuất tất cả phiên.
- Tự khóa sau số lần đăng nhập sai được cấu hình trong SQL.
- Kiểm tra trạng thái tài khoản và TokenVersion khi xác thực JWT.

Chưa có API hồ sơ, avatar và phân quyền theo dự án trong nhánh này.

## Yêu cầu

- .NET SDK hỗ trợ target net9.0 và .NET 9 Runtime.
- SQL Server 2017 trở lên.
- SSMS hoặc công cụ chạy script SQL.

## Tạo database

Trên database mới, chạy các script theo thứ tự:

1. Database/01_Schema.sql
2. Database/02_Logic.sql
3. Database/03_SeedData.sql
4. Database/07_AddTokenVersion.sql
5. Database/08_UpdateUserStatusTokenVersion.sql

Không chạy lại các script tạo bảng và seed trên database đã có dữ liệu.

Số thứ tự bị khuyết vì các script chức năng khác sẽ được thêm
ở những nhánh tiếp theo.

Tài khoản Admin trong seed chưa có mật khẩu sử dụng được.
Phần thiết lập mật khẩu sẽ được bổ sung ở nhánh xác thực.

## Cấu hình kết nối

Chạy từ thư mục gốc repository, thay TEN_MAY bằng SQL Server
instance trên máy của người cài:

```powershell
dotnet user-secrets set "ConnectionStrings:ScrumbanDb" "Server=TEN_MAY\SQLEXPRESS;Database=ScrumbanDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True" --project ./backend/MyTaigaAPI/MyTaigaAPI.csproj
```

TrustServerCertificate=True chỉ dành cho môi trường phát triển
với chứng chỉ SQL chưa được tin cậy.

User Secrets lưu trên từng máy, không được gửi kèm repository.

## Build và chạy

```powershell
dotnet build ./backend/MyTaigaAPI/MyTaigaAPI.csproj
dotnet run --project ./backend/MyTaigaAPI/MyTaigaAPI.csproj --launch-profile http
```

Xem địa chỉ thực tế ở dòng Now listening on trong Terminal.

Build thành công không đồng nghĩa đã kiểm tra kết nối SQL.
Nhánh này chưa có Controller nghiệp vụ để kiểm thử truy vấn.
## Cấu hình JWT

Chạy từ thư mục gốc repository:

```powershell
dotnet user-secrets set "Jwt:Issuer" "MyTaigaAPI" --project ./backend/MyTaigaAPI/MyTaigaAPI.csproj
dotnet user-secrets set "Jwt:Audience" "MyTaigaFrontend" --project ./backend/MyTaigaAPI/MyTaigaAPI.csproj
```

Tạo khóa ngẫu nhiên bằng PowerShell:

```powershell
$bytes = New-Object byte[] 64
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$key = [Convert]::ToBase64String($bytes)
dotnet user-secrets set "Jwt:Key" "$key" --project ./backend/MyTaigaAPI/MyTaigaAPI.csproj
Remove-Variable bytes, key
```

Mỗi máy cài đặt phải cấu hình secrets riêng. Không commit khóa JWT.

## Thiết lập mật khẩu Admin

Chạy công cụ:

```powershell
dotnet run --project ./backend/MyTaiga.PasswordTool/MyTaiga.PasswordTool.csproj
```

Nhập mật khẩu hai lần, lấy hash BCrypt rồi chạy trong SSMS:

```sql
USE ScrumbanDB;
GO

UPDATE dbo.[User]
SET PasswordHash = 'THAY_BANG_HASH_BCRYPT_THAT',
    UpdatedAt = SYSUTCDATETIME()
WHERE Email = 'admin@scrumban.local';
```

Chỉ chạy sau khi thay chuỗi mẫu bằng hash thật.
Không commit mật khẩu hoặc hash thật.

## Kiểm thử bản trên nhánh be/auth

Đã build thành công và kiểm thử tại localhost:5142:
- Đăng nhập USER thành công.
- Đăng xuất tất cả phiên thành công.
- Gọi lại logout bằng token vừa thu hồi nhận HTTP 401.

Các kiểm thử trên dùng database phát triển đã có tài khoản thử.
Chưa kiểm thử cài đặt lại toàn bộ từ database trống trên nhánh này.