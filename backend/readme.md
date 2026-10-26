# Backend MyTaiga — bàn giao Chương 2

ASP.NET Core net9.0, EF Core, SQL Server; chỉ backend. Nhánh mới nhất kế thừa các
nhánh chức năng trước. `main` không tự cập nhật nếu chưa merge.

- [Báo cáo kết quả](../docs/CHAPTER2_REPORT.md)
- [Đối chiếu 126 Use Case / 35 Epic](../docs/CHAPTER2_COVERAGE.md)
- [OpenAPI JSON để import Postman](../docs/openapi.json)
- Hợp đồng từng nhóm: `docs/testing-*.md` ở gốc repository.

## Cài đặt database

Cần .NET SDK hỗ trợ net9.0, SQL Server và sqlcmd/SSMS.
Trên máy chưa có ScrumbanDB, chạy các script trong Database theo thứ tự:
01, 02, 03, 07, 08, 09, 10, 11, 12, 13, 14, 15.
01 tạo database, 02 tạo logic, 03 nạp vai trò/cấu hình/Admin; không chạy lại 01–03
trên database có dữ liệu. Các script 07–15 nâng cấp theo thứ tự; sao lưu trước khi
áp dụng trên môi trường có dữ liệu quan trọng. sqlcmd cần `-I -b`.
Các số 04–06 không cần cho bản cài mới; dữ liệu mẫu và mật khẩu cá nhân không đưa lên Git.
Admin seed có hash giữ chỗ: sinh hash bằng PasswordTool và cập nhật PasswordHash cho
đúng admin trong SSMS. Không đặt mật khẩu hoặc mã khôi phục trong repository.

## Cấu hình trên từng máy

Chạy từ gốc repo. User Secrets không được Git đồng bộ; không gửi secrets.json.

```powershell
$project = '.\backend\MyTaigaAPI\MyTaigaAPI.csproj'
dotnet user-secrets set 'ConnectionStrings:ScrumbanDb' 'Server=.\SQLEXPRESS;Database=ScrumbanDB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True' --project $project
$key = New-Object byte[] 64
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($key); dotnet user-secrets set 'Jwt:Key' ([Convert]::ToBase64String($key)) --project $project } finally { $rng.Dispose(); Remove-Variable key }
dotnet user-secrets set 'Jwt:Issuer' 'MyTaigaAPI' --project $project
dotnet user-secrets set 'Jwt:Audience' 'MyTaigaFrontend' --project $project
dotnet user-secrets set 'Email:Provider' 'Development' --project $project
dotnet user-secrets set 'Frontend:ResetPasswordUrl' 'http://localhost:5173/reset-password' --project $project
dotnet user-secrets set 'Frontend:AllowedOrigins:0' 'http://localhost:5173' --project $project
dotnet build $project
dotnet run --project $project --launch-profile http --urls http://localhost:5142
```

TrustServerCertificate chỉ dành cho môi trường phát triển với chứng chỉ SQL tự ký.
JWT key mới sẽ làm token từ cấu hình cũ không còn hợp lệ. Không đổi key mỗi lần chạy.
Development lưu thư ở App_Data/dev-emails, chỉ dùng môi trường Development.
Gửi thật: Email:Provider=Smtp, Email:Smtp:Host=smtp.gmail.com, Port=587,
Username/FromAddress là tài khoản gửi, Password là App Password trong User Secrets,
FromName=MyTaiga. Thư gửi đến email tài khoản yêu cầu, không mặc định gửi về chủ SMTP.
Frontend:ResetPasswordUrl phải trỏ tới trang frontend thực tế. Ngoài máy local cần
URL công khai và HTTPS, không dùng localhost của người khác.

## API và realtime

Đăng ký/đăng nhập/forgot/reset là anonymous; các API còn lại yêu cầu Bearer JWT.
Role SYSTEM ADMIN/USER độc lập với PROJECT MANAGER/MEMBER; Admin không có quyền dự án ngầm.
Đăng xuất/đổi/reset mật khẩu/khóa tăng TokenVersion để thu hồi phiên.
Các phản hồi nghiệp vụ dùng success/message/data; validation tự động trả ProblemDetails.
HTTP 401 thiếu/token sai, 403 thiếu quyền, 404 không tìm thấy hoặc tài nguyên riêng tư,
409 xung đột nghiệp vụ, 400 validation.

- Tài khoản/hồ sơ: docs/testing-auth.md, luồng SMTP và reset link đã có.
- Dự án/thành viên/phòng ban; Epic/PBI; Sprint/Burndown; Task/Subtask/Board/WIP.
- Flow Metrics; Bug; bình luận/mention; tệp riêng tư; thông báo/nhật ký.
- Chat riêng/nhóm, file/preview, lịch sử/tìm kiếm, unread/read cursor.
- Admin người dùng/cấu hình, danh sách SYSTEM roles, API quyền theo dự án.

SignalR /hubs/notifications: notification.new và alias notificationReceived.
SignalR /hubs/chat: message.new và notification.new. Dùng accessTokenFactory;
access_token query chỉ chấp nhận cho hai hub. Không log query chứa bearer token.
Client nối lại phải tải REST inbox/history, khử trùng notificationId/messageId.
Outbox ít nhất một lần có thể phát lặp; PushedAt không phải biên nhận đã đọc.

Tệp/ảnh không nằm trong wwwroot; tải qua endpoint xác thực. Kho App_Data cần được
sao lưu cùng database. Không commit ảnh/tệp/thư thật. Có kiểm tra định dạng/giới hạn,
không thay thế phần mềm quét mã độc. Avatar cũ chưa có tác vụ dọn tự động.

## Kiểm thử

Database thử độc lập, không chạy script test trên ScrumbanDB thật.

```powershell
# Terminal 1, sau khi chuẩn bị database test theo docs/testing-projects.md
.\tests\start-isolated.ps1
# Terminal 2
.\tests\run-all.ps1
```

Test chạy HTTP + SQL + filesystem + WebSocket thật, không dùng Gmail thật.
Cấu hình test chỉ nằm trong tiến trình; thư/ảnh/tệp test ở App_Data/test-*.
Kết quả và giới hạn được ghi trong báo cáo. Frontend hiển thị biểu đồ/màu/thu gọn
cột và nhận link reset không nằm trong phần triển khai backend này.
