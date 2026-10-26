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
6. Database/09_UserCreatedAtUtc.sql
7. Database/10_SprintBurndownEvents.sql
8. Database/11_TaskUtcHistory.sql

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

## API hồ sơ cá nhân

Các endpoint đều yêu cầu Authorization: Bearer <token>.

- GET /api/v1/users/me: xem hồ sơ của chính mình.
- PUT /api/v1/users/me: cập nhật fullName, email, phoneNumber.
- PUT /api/v1/users/me/avatar: upload ảnh bằng multipart/form-data,
  trường File.
- GET /api/v1/users/me/avatar/{fileName}: đọc ảnh hiện tại của chính mình.

Avatar nhận JPEG/PNG tối đa 5 MiB, mỗi chiều tối đa 4096 pixel
và tổng tối đa 16 triệu pixel. Backend xuất lại thành PNG.
File lưu tại App_Data/avatars và không đưa lên Git.

Frontend cần gửi JWT khi tải ảnh, nhận blob rồi tạo object URL.

## Script bổ sung cho hồ sơ

Sau các script trước đó, chạy:
Database/09_UserCreatedAtUtc.sql

Script đổi mặc định User.CreatedAt cho bản ghi mới sang UTC,
không chuyển đổi thời gian của dữ liệu cũ.

## Kiểm thử nhánh be/profile

Đã kiểm thử trên bản repo mới tại cổng 5142:

- Upload JPEG thành công.
- Đọc ảnh trả HTTP 200 và Content-Type image/png.
- Đường dẫn avatar được lưu và trả về trong hồ sơ.
- Đọc ảnh không có JWT trả HTTP 401.

Xem và sửa hồ sơ đã được kiểm thử trên backend gốc.
Chưa kiểm thử đầy đủ các trường hợp file lỗi, quá dung lượng,
sai định dạng và truy cập ảnh của tài khoản khác trên nhánh mới.
Chưa có cơ chế dọn file avatar cũ.
**Đúng: thiếu JWT bị chặn bằng HTTP 401** ✅

Phần khóa/mở khóa đã kiểm thử được: USER bị chặn 403, Admin khóa/mở khóa thành công, token cũ bị thu hồi, tự khóa Admin bị chặn 409 và tài khoản không tồn tại trả 404.

Giờ thêm cuối **`backend/readme.md`**:

```markdown
## API Admin khóa và mở khóa tài khoản

Yêu cầu JWT của tài khoản ADMIN. Không cần request body.

- PATCH /api/v1/admin/users/{userId}/lock
- PATCH /api/v1/admin/users/{userId}/unlock

Khóa tài khoản tăng TokenVersion để thu hồi toàn bộ token cũ.
Mở khóa đặt FailedLoginCount về 0 và không khôi phục token cũ.
Các thao tác được ghi vào ActivityLog.

Mã phản hồi:
- 200: thành công.
- 400: userId không hợp lệ.
- 401: thiếu JWT, JWT không hợp lệ hoặc đã bị thu hồi.
- 403: không có quyền Admin.
- 404: tài khoản không tồn tại.
- 409: Admin cố khóa chính mình.

Đã kiểm thử trên cổng 5142:
- USER gọi lock nhận 403.
- Admin khóa và mở khóa tài khoản thử thành công.
- Token USER cũ nhận 401 cả sau khi mở khóa.
- Admin tự khóa nhận 409.
- Tài khoản không tồn tại nhận 404.
- Không gửi JWT nhận 401.

Chưa kiểm thử riêng userId không hợp lệ và USER gọi unlock.
```
## Quên mật khẩu và đặt lại mật khẩu

- POST /api/v1/auth/forgot-password
  Body: {"email":"test.user@scrumban.local"}
  Trả thông báo chung cho email tồn tại và không tồn tại.

- POST /api/v1/auth/reset-password
  Body: {"token":"<ma-trong-email>","newPassword":"<mat-khau-moi>","confirmPassword":"<nhap-lai-mat-khau-moi>"}

Mã đặt lại mật khẩu có thời hạn, chỉ dùng một lần.
Database chỉ lưu SHA-256 của mã.
Đặt lại mật khẩu thành công sẽ thu hồi các phiên đăng nhập cũ.

Trong môi trường Development, email được ghi vào:
backend/MyTaigaAPI/App_Data/dev-emails/

Các file email thử không được đưa lên Git.
Mỗi máy tự sinh các file này khi gọi API.
Chưa tích hợp SMTP gửi email thật; chưa sẵn sàng cho Production.

Đã kiểm thử:
- Yêu cầu bằng email hợp lệ tạo file email thử.
- Email không tồn tại nhận cùng thông báo chung.
- Đặt lại mật khẩu thành công.
- Dùng lại mã bị từ chối với HTTP 400.
- Đăng nhập bằng mật khẩu mới thành công.
- Xác nhận mật khẩu không khớp trả HTTP 400 với lỗi ConfirmPassword.
## Đổi mật khẩu khi đang đăng nhập

POST /api/v1/auth/change-password
Authorization: Bearer <accessToken>

Body:
{
  "currentPassword": "<mat-khau-hien-tai>",
  "newPassword": "<mat-khau-moi>",
  "confirmPassword": "<nhap-lai-mat-khau-moi>"
}

Đổi thành công sẽ thu hồi tất cả phiên đăng nhập
và vô hiệu hóa các mã đặt lại mật khẩu chưa sử dụng.
Người dùng phải đăng nhập lại.

Đã kiểm thử:
- Không có token: HTTP 401.
- Đổi mật khẩu hợp lệ: HTTP 200.
- Token cũ sau khi đổi: HTTP 401.
- Đăng nhập bằng mật khẩu mới: thành công.
- Mật khẩu hiện tại sai: HTTP 400.
## Gửi email qua SMTP

Email:Provider chọn cách gửi:
- Development: ghi email vào App_Data/dev-emails, chỉ dùng trong Development.
- Smtp: gửi email thật qua MailKit, cổng 587 với STARTTLS.

Cấu hình SMTP bằng User Secrets hoặc biến môi trường:
- Email:Provider = Smtp
- Email:Smtp:Host = smtp.gmail.com
- Email:Smtp:Port = 587
- Email:Smtp:Username = địa chỉ Gmail gửi
- Email:Smtp:Password = mật khẩu ứng dụng Google
- Email:Smtp:FromAddress = địa chỉ Gmail gửi
- Email:Smtp:FromName = MyTaiga

Không đưa mật khẩu vào mã nguồn hoặc Git.
Mỗi máy cần tự cấu hình; User Secrets không đi kèm repository.
Gmail cần bật xác minh 2 bước và tạo mật khẩu ứng dụng.
Tài khoản nhận thư phải dùng địa chỉ email thật.

Để thử bằng file trên máy:
```powershell
dotnet user-secrets set "Email:Provider" "Development" --project .\backend\MyTaigaAPI\MyTaigaAPI.csproj
```

Đã kiểm thử nhận email thật qua Gmail và dùng mã trong thư
để đặt lại mật khẩu thành công.

Email chứa liên kết khôi phục có mã token
Chưa tích hợp liên kết tới trang đặt lại mật khẩu của frontend.
## Liên kết khôi phục mật khẩu

Cấu hình Frontend:ResetPasswordUrl bằng User Secrets
hoặc biến môi trường.

Ví dụ chạy trên máy:
http://localhost:5173/reset-password

Backend gửi liên kết dạng:
<Frontend:ResetPasswordUrl>?token=<ma-khoi-phuc>

URL cấu hình không chứa query hoặc fragment.
Dùng HTTPS khi triển khai; HTTP chỉ dành cho địa chỉ loopback.

Frontend cần đọc token từ liên kết và gọi:
POST /api/v1/auth/reset-password

Body gồm: token, newPassword, confirmPassword.

Đã kiểm thử nhận email Gmail chứa liên kết khôi phục.
Chưa kiểm thử toàn bộ luồng qua giao diện frontend.
## Đăng ký tài khoản

POST /api/v1/auth/register
Không yêu cầu token.

Body:
{
  "fullName": "Người dùng mới",
  "email": "user@example.com",
  "password": "<mat-khau>",
  "confirmPassword": "<nhap-lai-mat-khau>"
}

Tài khoản mới được cấp vai trò USER thuộc SYSTEM.
Mật khẩu lưu dưới dạng BCrypt.
Đăng ký thành công không tự đăng nhập.
Chưa tích hợp xác minh email.

Đã kiểm thử:
- Đăng ký mới: HTTP 201, trả userId và systemRole USER.
- Đăng nhập bằng tài khoản mới: thành công.
- Email trùng: HTTP 409.
- Xác nhận mật khẩu không khớp: HTTP 400, lỗi ConfirmPassword.
# Thành viên dự án

Phòng ban UC6.1–UC6.4 đã có thêm/sửa/xóa, danh sách và gán/chuyển/bỏ gán thành viên.
Xem `docs/testing-departments.md`; 39 kiểm tra phòng ban và 81 kiểm tra hồi quy đã đạt.

Đã bổ sung UC5.1–UC5.5: thêm/xóa, danh sách/tìm kiếm/lọc/phân trang, nâng Member lên Manager.
Xem `docs/testing-members.md`. Đã kiểm thử 39 trường hợp và chạy lại 42 kiểm tra dự án.

# Quản lý dự án

Task UC13–UC15 có CRUD/Subtask, phân công, tiến độ, deadline/priority và API cảnh báo.
Xem `docs/testing-tasks.md`; chạy script 11 sau 10. 61 kiểm tra thực tế đã đạt.

Sprint UC11–UC12 đã có lập kế hoạch, chọn PBI, bắt đầu/đóng, tiến độ, Goal và dữ liệu Burndown.
**Chạy Database/10_SprintBurndownEvents.sql sau script 09 trước khi dùng bản mới.**
Xem `docs/testing-sprints.md`; 58 kiểm tra Sprint và 201 kiểm tra hồi quy đã đạt ngày 2026-10-26.
Thông báo Sprint đã lưu trong database; SignalR thuộc nhóm chức năng tiếp theo.

Product Backlog UC7–UC8 đã có CRUD, tìm/lọc/phân trang, ưu tiên và sắp xếp PBI trong Epic.
Xem `docs/testing-backlog.md`; 43 kiểm tra Backlog và 38 kiểm tra Epic hồi quy đã đạt.

Epic UC9–UC10 đã có CRUD, danh sách/lọc, tiến độ PBI/Task và liên kết công việc cùng dự án.
Xem `docs/testing-epics.md`; 38 kiểm tra HTTP + SQL thực đã đạt.

Đã bổ sung API tạo, danh sách có phân trang/lọc, chi tiết, cập nhật, đóng và xóa mềm dự án
(UC4.1–UC4.5). Người tạo là MANAGER; thành viên chỉ đọc, Admin không có quyền ngầm.
Xem `docs/testing-projects.md` để lấy request/response, quy tắc và cách chạy kiểm thử.
Đã chạy 42 kiểm tra HTTP + SQL thực trên database riêng ngày 2026-10-25.
