# Phòng ban dự án (UC6.1–UC6.4)

Bearer JWT bắt buộc. Manager quản lý; thành viên dự án được xem danh sách.

| Method | URL | Body |
|---|---|---|
| GET | /api/v1/projects/{id}/departments?page=1&pageSize=20 | Không |
| POST | /api/v1/projects/{id}/departments | departmentName, description |
| PUT | /api/v1/projects/{id}/departments/{departmentId} | departmentName, description |
| DELETE | /api/v1/projects/{id}/departments/{departmentId} | Không |
| PATCH | /api/v1/projects/{id}/members/{userId}/department | `{ "departmentId": 1 }` hoặc null để bỏ gán |

Tên bắt buộc, trim, tối đa 100; mô tả tùy chọn tối đa 500 (khớp schema SQL).
POST trả 201; các thao tác khác trả 200. Response luôn có success,message,data.
Danh sách data={items,total,page,pageSize}; item gồm departmentId,projectId,departmentName,
description,memberCount. Mutation phòng ban trả departmentId/projectId/name/description;
gán phòng ban trả projectId/userId/departmentId.

400 dữ liệu sai; 401 chưa xác thực; 403 sai quyền; 404 không có entity trong đúng dự án;
409 trùng tên, đã gán như yêu cầu, xóa phòng ban còn thành viên hoặc sửa dự án đã đóng.
Mã phòng ban của dự án khác không được dùng cho sửa/xóa/gán thành viên.
Trước khi xóa, cần chuyển hoặc bỏ gán các thành viên; không tự xóa thành viên.
Mỗi mutation và audit nằm cùng transaction, tuần tự hóa theo dự án.

## Kiểm thử

Chạy trên database riêng theo `docs/testing-projects.md`:

```powershell
python tests/departments_integration.py
python tests/members_integration.py
python tests/projects_integration.py
```

2026-10-25: 39 kiểm tra phòng ban đạt, 39 thành viên và 42 dự án chạy hồi quy đều đạt.
Gồm phân quyền anonymous/Admin/outsider/Member/Manager, độ dài theo schema,
duplicate/rollback, phân trang, chống thao tác chéo dự án, gán/chuyển/bỏ gán,
chặn xóa phòng ban đang sử dụng, bảo vệ CLOSED/DELETED và audit đúng số lần.
