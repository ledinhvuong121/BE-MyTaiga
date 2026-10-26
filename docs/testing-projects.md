# API dự án (UC4.1–UC4.5)

Tất cả endpoint yêu cầu Bearer JWT. Admin không có quyền ngầm trong dự án.

| Method | URL | Quyền | Kết quả |
|---|---|---|---|
| POST | /api/v1/projects | SYSTEM USER | 201, người tạo thành MANAGER |
| GET | /api/v1/projects?page=1&pageSize=20&keyword=&status=ACTIVE | Đã đăng nhập | 200, chỉ dự án đã tạo/tham gia |
| GET | /api/v1/projects/{id} | Thành viên dự án | 200 |
| PUT | /api/v1/projects/{id} | MANAGER của dự án ACTIVE | 200 |
| PATCH | /api/v1/projects/{id}/close | MANAGER của dự án ACTIVE | 200 |
| DELETE | /api/v1/projects/{id} | MANAGER | 200, xóa mềm |

POST và PUT nhận cùng body (PUT thay thế các trường này):

```json
{
  "projectName": "MyTaiga",
  "description": "Dự án quản lý công việc",
  "startDate": "2026-10-25",
  "endDate": "2026-12-25"
}
```

Tên bắt buộc, tối đa 200 ký tự; mô tả tối đa 10000; ngày kết thúc không trước ngày bắt đầu.
Không nhận CreatedBy, Status hoặc vai trò từ người gọi. Tên duy nhất trong phạm vi người tạo,
kể cả dự án đã xóa mềm (theo unique constraint hiện tại).

Response chi tiết: `{success,message,data:{projectId,projectName,description,startDate,endDate,status,createdBy,projectRole,createdAt,updatedAt}}`.
Response danh sách: `{success,message,data:{items:[],total,page,pageSize}}`.
Ngày giờ trả về UTC; DateOnly trả yyyy-MM-dd. Danh sách sắp xếp CreatedAt DESC, ProjectId DESC;
page >= 1, pageSize 1–100; status chỉ nhận ACTIVE/CLOSED. Không trả DELETED.

401: thiếu/hết hạn/thu hồi token; 403: sai quyền; 404: không tồn tại hoặc đã xóa;
409: trùng tên, sửa dự án đã đóng hoặc đóng lại; 400: dữ liệu/tham số sai.
DELETE giữ nguyên dữ liệu liên quan; không tự mở lại dự án đã đóng.
SQL trigger hiện có tạo bốn cột, Manager và một log tạo; các thao tác còn lại
ghi ActivityLog trong cùng transaction. Cần chạy 02_Logic.sql khi dựng database.

## Kiểm thử thực tế, độc lập dữ liệu người dùng

Script `tests/projects_integration.py` dùng Python standard library và `sqlcmd`.
Chỉ dùng với database riêng có tên dạng `MyTaiga_*Checks_*`. Script tạo dữ liệu và giữ lại
để tra cứu; KHÔNG chạy vào ScrumbanDB. Không cần mật khẩu thật của Admin:
JWT giả lập Admin chỉ hợp lệ với khóa kiểm thử riêng bên dưới.

1. Tạo database tên mới, ví dụ `MyTaiga_ProjectChecks_20261025`:
   lấy bản sao các script 01,02,03,07,08,09,10,11 vào thư mục tạm,
   thay tên `ScrumbanDB` bằng tên database kiểm thử rồi chạy theo thứ tự.
   Không sửa script nguồn và không chạy script 01 lên database đã tồn tại.
2. Trong PowerShell riêng, cấu hình biến môi trường **chỉ cho tiến trình thử**:

```powershell
$env:ConnectionStrings__ScrumbanDb='Server=.\SQLEXPRESS;Database=MyTaiga_ProjectChecks_20261025;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
$env:Jwt__Key='integration-test-only-key-not-for-production-20261025'
$env:Jwt__Issuer='MyTaigaIntegration'
$env:Jwt__Audience='MyTaigaIntegration'
$env:Email__Provider='Development'
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --no-launch-profile --project backend/MyTaigaAPI/MyTaigaAPI.csproj --urls http://127.0.0.1:5159
```

Không sử dụng khóa thử này để triển khai hoặc lưu đè User Secrets thật.
Nếu chọn database/port khác, đặt TEST_DATABASE/TEST_API_URL tương ứng trước khi chạy script.

3. Từ Terminal khác tại repo:

```powershell
python tests/projects_integration.py
```

Kết quả ngày 2026-10-25: 42 kiểm tra đạt trên SQL Server Express thực, database riêng.
Bao gồm anonymous/Admin/outsider/Member/Manager, lỗi dữ liệu, phân trang,
lọc, kiểm tra bốn cột + Manager + log từ trigger, rollback rename conflict,
đóng/xóa mềm giữ dữ liệu, hai request đồng thời và token bị thu hồi.

Giới hạn: đây là kiểm thử chức năng/tích hợp, không phải benchmark tải lớn.
Backend mới tạo không dùng tài khoản hoặc ảnh thật; email được đặt Development.
