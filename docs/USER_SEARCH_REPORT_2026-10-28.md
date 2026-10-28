# API tìm kiếm tài khoản cho người đăng nhập — 28/10/2026

Nhánh `be/user-search`, kế thừa `be/cloudinary-missing-data`.

## Frontend gọi

`GET /api/v1/users?keyword=user5%40gmail.com&page=1&pageSize=20`

Header: `Authorization: Bearer <accessToken>`.
Mọi tài khoản đang hoạt động đã đăng nhập đều được gọi, không cần quyền Admin.
Tìm chứa từ khóa trong FullName hoặc Email, trim khoảng trắng. Bỏ keyword để lấy
trang danh bạ. Chỉ trả tài khoản Status=1; pageSize tối đa 100. Thứ tự FullName, UserId.

Response ví dụ minh họa (ID/tên phụ thuộc dữ liệu thật):

```json
{
  "success": true,
  "message": "Tìm kiếm tài khoản thành công.",
  "data": {
    "items": [{"userId": 5, "fullName": "Người dùng", "email": "user5@gmail.com", "avatarUrl": null}],
    "total": 1,
    "page": 1,
    "pageSize": 20
  }
}
```

Không tìm thấy: HTTP 200, items=[], total=0. Chưa đăng nhập: 401.
Query sai giới hạn/keyword dài hơn 200 ký tự: 400.
Không trả hash mật khẩu, số lần đăng nhập sai, token version hoặc dữ liệu quản trị.
Endpoint Admin `/api/v1/admin/users` vẫn chỉ dành cho Admin.

FE thay request tìm kiếm trong popup thêm thành viên từ `/api/v1/admin/users`
sang `/api/v1/users`; dùng data.items để hiển thị và userId để gọi API thêm thành viên
hiện có. Quyền thêm thành viên vẫn được kiểm tra riêng tại endpoint dự án.
Danh bạ này chưa có khái niệm tổ chức/tenant vì backend hiện chưa có mô hình đó;
nhãn TechCorp/demo trong ảnh không được dùng để suy đoán phạm vi dữ liệu.

## Kiểm chứng

- Build: 0 lỗi, 0 cảnh báo.
- 17 kiểm tra HTTP/SQL thật trên database MyTaiga_ProjectChecks_20261025: PASS.
  Gồm tên tiếng Việt, email, từ khóa rỗng/không khớp, trim, phân trang, giới hạn,
  tài khoản khóa, dữ liệu response tối thiểu, quyền USER/Admin/anonymous, route hồ sơ.
- 39 kiểm tra hồi quy quản lý thành viên: PASS.
- OpenAPI đã cập nhật route GET /api/v1/users.
- Không chạy thao tác ghi trên database ScrumbanDB thật; không thay frontend.

[Log tìm kiếm](test-logs/user-search-2026-10-28.txt) ·
[Log thành viên](test-logs/user-search-members-2026-10-28.txt).

Bảng kiểm thử FE người dùng cung cấp có 42 mục BLOCKED nhưng không kèm chi tiết
nguyên nhân; thay đổi này xử lý yêu cầu tìm kiếm user, chưa xác nhận giải quyết toàn bộ 42 mục.
