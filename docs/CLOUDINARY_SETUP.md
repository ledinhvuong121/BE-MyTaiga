# Cấu hình Cloudinary — không gửi bí mật vào chat/Git

1. Tạo tài khoản tại https://cloudinary.com và mở Console.
2. Trong phần API Keys của product environment, lấy Cloud name, API Key và API Secret.
3. Mở PowerShell tại thư mục BE-MyTaiga-Git, chạy:

```powershell
.\scripts\configure-cloudinary.ps1
```

Nhập ba thông tin theo lời nhắc. Secret được nhập kín và chỉ lưu ở User Secrets của
máy hiện tại. Không sửa API Secret vào appsettings, README, request frontend hoặc Git.
Dừng backend đang chạy bằng Ctrl+C rồi chạy lại:

```powershell
dotnet run --project .\backend\MyTaigaAPI\MyTaigaAPI.csproj --launch-profile http --urls http://localhost:5142
```

Đăng nhập rồi gọi PUT /api/v1/users/me/avatar với multipart field **file** là ảnh trên
máy. Kết quả data.avatar phải là https://res.cloudinary.com/<cloud>/image/upload/...
Ảnh Task: POST /api/v1/tasks/{id}/attachments với field files.
Ảnh chat: POST /api/v1/chat/rooms/{id}/messages multipart files (+ content tùy chọn).

Database lưu URL HTTPS chuẩn hóa từ secure_url; bỏ đoạn chữ ký CDN nếu Cloudinary trả kèm. URL avatar dùng trực tiếp trong img src, không
cần bearer trong thẻ img. Avatar là ảnh nhận diện công khai cho những ai biết URL.
Ảnh Task/chat upload authenticated; API trả URL cloud có chữ ký hết hạn sau 10 phút.
Frontend lấy lại metadata khi URL hết hạn, không lưu URL có chữ ký vào database.
Link đã cấp có thể dùng đến hết hạn dù quyền dự án bị thu hồi trong khoảng đó.
Nếu cần thu hồi ngay mọi link đã cấp, cần delivery token/kiểm tra qua gateway riêng.
File không phải ảnh (PDF/Office/ZIP/TXT) vẫn dùng luồng riêng tư trên server.

## Chuyển ảnh cũ

Chưa tự chuyển ảnh thật khi Cloudinary chưa được cấu hình. Admin có thể gọi:

POST /api/v1/admin/media/migrate-images

```json
{"execute":false,"batchSize":10}
```

Đổi execute=true để upload từng ảnh cũ và thay cột URL. Mỗi ảnh được xử lý độc lập;
xem status từng dòng. File gốc giữ lại để sao lưu. Truyền các giá trị data.next vào
request kế tiếp: afterUserId, afterAttachmentId, afterMessageAttachmentId. Cursor
cho phép đi qua file nguồn đã mất mà không bị lặp vô hạn ở batch đầu.
Không bỏ qua status lỗi; SOURCE_MISSING cần tìm lại file gốc trên máy đã upload trước.
Không thể khôi phục nội dung ảnh chỉ từ tên GUID nếu file gốc không còn.

## Kiểm thử an toàn

Không dùng tài khoản Cloudinary thật cho hồi quy tự động:

```powershell
# Terminal 1: Cloudinary test double, chỉ loopback
python .\tests\fake_cloudinary.py
# Terminal 2: API/database thử
.\tests\start-isolated.ps1 -CloudinaryStub
# Terminal 3
.\tests\run-all.ps1
```

ApiBase override chỉ cho loopback trong Development; production chỉ gọi
https://api.cloudinary.com. Không đặt Cloudinary:ApiBase giả lập vào User Secrets.
Test double xác minh chữ ký SHA-256, lưu bytes trong RAM và mô phỏng lỗi. Đây không
phải bằng chứng upload tài khoản Cloudinary thật. Lượt thật cần cấu hình phía trên.

Nguồn API chính thức:
- https://cloudinary.com/documentation/image_upload_api_reference
- https://cloudinary.com/documentation/authentication_signatures
- https://cloudinary.com/documentation/control_access_to_media

## Nếu đã nhập nhầm dấu sao làm Secret

Không sửa file script và không dán dấu sao đang hiển thị trên trang Cloudinary.
Dùng chức năng Copy hoặc hiện giá trị API Secret thật trong View API Keys, rồi chạy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "V:\University\Đồ Án Chuyên Ngành\BE-MyTaiga-Git\scripts\configure-cloudinary.ps1" -SecretOnly
```

Chế độ này giữ nguyên Cloud name/API Key, chỉ hỏi lại Secret và không chấp nhận
chuỗi dấu sao. Nội dung nhập được che; không gửi giá trị thật vào chat hoặc ảnh chụp.

Đã bỏ hoàn toàn JSON stdin để tránh lỗi 0xEF/BOM trên Windows PowerShell. Script gọi dotnet user-secrets set cho từng khóa. Đã kiểm thử chính file configure-cloudinary.ps1 trên Windows PowerShell 5.1 với console UTF-8 BOM, xác nhận lưu và đọc lại bằng User Secrets giả lập riêng; không dùng hoặc in Secret thật.

## Kết quả trên máy chủ dự án ngày 27/10/2026

Đã cấu hình và kiểm thử Cloudinary thật; 1 avatar cũ đã chuyển, giữ nguồn.
Xem [báo cáo thực nghiệm](CLOUDINARY_LIVE_REPORT_2026-10-27.md).
Kiểm thử cloud thật có opt-in: khởi động tests/start-isolated.ps1 với -RealCloudinary
và database MyTaiga_*Checks_*, rồi chạy tests/cloudinary_live_integration.py
--allow-real-cloud. Kiểm thử dùng quota thật và tạo manifest dọn asset tại App_Data;
chỉ dọn asset trong manifest của database kiểm thử, không dọn ảnh người dùng thật.
