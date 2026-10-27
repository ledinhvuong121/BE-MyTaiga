# Thay đổi ảnh ngày 27/10

Avatar mới là URL Cloudinary public để hiển thị nhận diện. Luồng avatar local có xác
thực chỉ giữ cho dữ liệu cũ chưa chuyển. Bộ auth đã cập nhật assertion theo hợp đồng
cloud; chạy fake_cloudinary.py và start-isolated.ps1 -CloudinaryStub trước kiểm thử.
Xem CLOUDINARY_SETUP.md và MISSING_DATA_UPDATE_REPORT_2026-10-27.md.

# Kiểm thử tài khoản, avatar và CORS

Chạy `python tests/auth_integration.py` với máy chủ test cô lập. Dữ liệu gồm tài khoản
mới sinh ngẫu nhiên, database riêng. Thư reset và avatar test ở App_Data/test-*;
không dùng SMTP hoặc đổi User Secrets thật.

Kiểm tra đăng ký trùng/validation; BCrypt; đăng nhập sai/bộ đếm/reset bộ đếm;
đúng mật khẩu, profile/điện thoại, avatar giải mã/PNG/giới hạn/quyền;
đổi mật khẩu và thu hồi JWT; mã reset mới thay mã cũ, single-use, hết hạn,
reset đồng thời; khóa tự động sau năm lần sai, Admin mở khóa; logout;
CORS frontend được phép và origin ngoài danh sách bị chặn.

CORS mặc định http://localhost:5173. Cấu hình Frontend:AllowedOrigins dạng array
trên từng môi trường; không dùng wildcard khi gửi credentials.

DevelopmentEmailSender đọc Email:DevelopmentDirectory tùy chọn. Avatar dùng
Storage:AvatarsRoot tùy chọn. Mặc định vẫn giữ kho App_Data hiện có.

Kết quả cuối: 36 kiểm tra đạt, log ở test-logs/2026-10-26/auth.txt.
