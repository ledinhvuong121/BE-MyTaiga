# Backend MyTaiga

## Phạm vi hiện tại

Khung ASP.NET Core Web API và Entity Framework Core ánh xạ
database SQL Server. Chưa có API đăng nhập hoặc hồ sơ ở nhánh này.

Repository này chỉ chứa backend và script SQL của MyTaiga.

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
