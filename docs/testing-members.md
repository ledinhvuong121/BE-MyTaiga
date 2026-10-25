# Thành viên dự án (UC5.1–UC5.5)

Bearer JWT bắt buộc. Quyền MANAGER/MEMBER luôn xét trong đúng dự án.

| Method | URL | Body / quyền |
|---|---|---|
| GET | /api/v1/projects/{id}/members?page=1&pageSize=20&keyword=&departmentId= | Thành viên; tìm theo họ tên, lọc phòng ban |
| POST | /api/v1/projects/{id}/members | `{ "userId": 3 }`; Manager, trả 201, luôn gán MEMBER |
| DELETE | /api/v1/projects/{id}/members/{userId} | Manager, trả 200 |
| PATCH | /api/v1/projects/{id}/members/{userId}/role | `{ "role": "MANAGER" }`; Manager, trả 200 |

Danh sách trả `{success,message,data:{items,total,page,pageSize}}`; mỗi item có
userId,fullName,email,avatar,status,projectRole,departmentId,departmentName,joinedAt.
Không trả PasswordHash/TokenVersion. page>=1, pageSize 1–100; sắp theo tên rồi userId.
Mutation trả `{success,message,data:{projectId,userId,projectRole}}`; role null khi loại bỏ.

400: ID/dữ liệu không hợp lệ, cố gán role khác MANAGER hoặc thêm Admin;
401: chưa xác thực; 403: không có quyền trong dự án;
404: không có dự án/user/thành viên/phòng ban;
409: đã thuộc dự án/đã là Manager, tài khoản bị khóa, dự án đã đóng,
loại Manager cuối cùng hoặc còn Task chưa DONE đang giao cho người bị loại bỏ.

Chỉ thêm tài khoản SYSTEM USER đang hoạt động. Manager có thể rời dự án nếu còn Manager khác.
Chưa hỗ trợ hạ vai trò vì UC5.5 chỉ yêu cầu nâng Member thành Manager.
Quy tắc bổ sung để bảo toàn công việc: cần chuyển giao Task chưa hoàn thành trước khi loại bỏ;
không xóa lịch sử Task đã hoàn thành. Thay đổi dự án đã CLOSED/DELETED bị chặn.
Mỗi thay đổi ghi một ActivityLog cùng transaction. Khóa dự án tuần tự hóa mutation và bảo vệ
trường hợp hai Manager tự loại bỏ đồng thời. Trigger KeepManager vẫn bảo vệ ở SQL.

## Kiểm thử

Dựng API/database riêng theo `docs/testing-projects.md`, sau đó:

```powershell
python tests/members_integration.py
python tests/projects_integration.py
```

Ngày 2026-10-25: 39 kiểm tra thành viên đạt; 42 kiểm tra hồi quy dự án đạt.
Kiểm tra HTTP + SQL thực gồm các vai trò, thao tác sai quyền, user không tồn tại/bị khóa,
chống tự nâng quyền, lọc/phân trang, Task chưa hoàn thành, mất quyền ngay sau loại bỏ,
nâng Manager có hiệu lực ngay, không mất Manager cuối cùng khi hai request đồng thời,
đóng/xóa dự án và số ActivityLog đúng. Script tạo dữ liệu trong database thử, không gửi email.
