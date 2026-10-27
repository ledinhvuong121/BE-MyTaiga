# Báo cáo Cloudinary thật và chuyển ảnh cũ — 27/10/2026

Nhánh: `be/cloudinary-missing-data`. Backend MyTaiga.

## Kết quả

- Đã lưu cấu hình Cloudinary trong User Secrets trên máy chủ dự án, không lưu Secret vào Git.
- Smoke test dịch vụ thật: upload avatar, ảnh Task và ảnh Chat; đọc ảnh HTTP 200.
- 5 nhóm kiểm tra API thật trên `MyTaiga_CloudInstallChecks_20261027` đều đạt:
  1. PUT avatar từ máy lên cloud, đọc ảnh công khai 200, User.Avatar chỉ lưu URL.
  2. POST ảnh Task, DB lưu URL authenticated chuẩn; signed preview/download trả đúng bytes.
  3. URL private gốc không có quyền trả 401; không công khai ảnh Task.
  4. Metadata Task từ chối người ngoài dự án 403 và không đăng nhập 401.
  5. POST ảnh Chat, DB lưu URL cloud chuẩn; signed preview/download trả đúng bytes.
- Sau bản sửa URL, chạy lại 154 kiểm tra: missing_data 29, auth 36, attachments 33,
  chat 33, notifications 23; tất cả PASS. Bộ đầy đủ trước đó gồm 722 kiểm tra,
  18 suites; lần này chạy lại các bộ chịu ảnh hưởng, không cộng trùng số lượng.
- Build cuối: 0 lỗi, 0 cảnh báo.

## Thay đổi từ kiểm thử thật

Cloudinary trả secure_url của ảnh authenticated có đoạn chữ ký `s--...--`.
Backend đã được sửa để nhận đúng public_id, bỏ chữ ký CDN khi lưu database và
chỉ cấp URL download có thời hạn sau khi kiểm tra quyền. Test double nay mô phỏng
đúng phản hồi này; bộ kiểm thử cũng không gửi JWT backend sang host cloud.
Avatar là ảnh công khai; ảnh Task/Chat dùng authenticated và signed URL hạn 10 phút.

## Dữ liệu thật ScrumbanDB

Đã chuyển avatar cũ của UserId 2: MIGRATED. Giá trị mới là URL HTTPS Cloudinary,
đọc ảnh HTTP 200. File nguồn vẫn được giữ. Không có ảnh Task/Chat cũ để chuyển.
Không thay mật khẩu hoặc vai trò tài khoản.

Mapping trước chuyển được sao lưu ngoài Git tại:
`C:\Users\ASUS\AppData\Local\MyTaiga\MigrationBackups\cloudinary-images-20261027T082146172.json`.
Bản sao này cùng file ảnh gốc cho phép đối chiếu/khôi phục nếu cần.
Script 16 đã áp dụng trước đó, xem [biên bản schema](MIGRATION16_APPLIED_2026-10-27.md).

Đã gửi lệnh destroy cho 6 ảnh thử của smoke và API; không xóa avatar thật vừa chuyển.
Chưa kiểm chứng độc lập việc purge cache/xóa asset phía nhà cung cấp.

## Bàn giao và giới hạn

Code nằm trên nhánh hiện tại, chưa tự merge main. Máy khác phải cấu hình Cloudinary
riêng, các giá trị User Secrets không đi theo Git. Sau khi lấy code mới cần khởi động
lại backend. File PDF/Office/ZIP/TXT tiếp tục lưu riêng trên backend; thay đổi cloud
áp dụng cho ảnh theo yêu cầu.

Signed URL đã cấp là capability link, có thể còn sử dụng đến hết hạn dù quyền tài khoản
thay đổi; API kiểm tra quyền khi cấp URL mới. Chưa kiểm thử tải/quota diện rộng,
khả năng phục hồi khi Cloudinary mất kết nối dài hoặc toàn bộ UI frontend.

Bằng chứng: [kết quả JSON](cloudinary-live-results-2026-10-27.json),
[log hồi quy](test-logs/cloud-2026-10-27/),
[báo cáo tổng hợp missing data](MISSING_DATA_UPDATE_REPORT_2026-10-27.md).
