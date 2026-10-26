# UC33–UC35: quản trị và quyền

29 kiểm tra HTTP/SQL đạt. GET /api/v1/admin/users?keyword=&status=&page=&pageSize=;
POST cùng URL nhận fullName,email,password,confirmPassword, luôn tạo USER.
GET /api/v1/admin/roles chỉ trả SYSTEM. PUT /api/v1/admin/users/{id}/role nhận
{"role":"USER"}. Giữ quy tắc đúng một Admin: không cấp Admin thứ hai hoặc gỡ Admin
cuối cùng. Đề xuất chuyển quyền Admin cần luồng bàn giao riêng, không thực hiện ngầm.

GET/PUT /api/v1/admin/system-config. PUT nhận {"values":{"MAX_COMMENT_LENGTH":"5000"}}.
Whitelist chín khóa seed, giới hạn giá trị, kiểm tra cả batch trước cập nhật, không nhận secret.
GET /api/v1/admin/activity-logs chỉ nhật ký hệ thống không lộ nhật ký dự án.
GET /api/v1/me/permissions?projectId= trả quyền hệ thống và quyền đúng dự án hiện tại.
Manager kế thừa Member; Admin không có quyền vượt rào dự án.
