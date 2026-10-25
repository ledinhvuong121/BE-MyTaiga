# Epic và liên kết (UC9.1–UC9.4, UC10.1–UC10.3)

Tất cả cần Bearer JWT. Thành viên trong dự án được đọc; chỉ Manager được ghi.

| Method | URL | Body |
|---|---|---|
| GET | /api/v1/projects/{id}/epics?page=1&pageSize=20&keyword=&status=OPEN | Không |
| POST | /api/v1/projects/{id}/epics | title, description, priority, status |
| GET | /api/v1/epics/{id} | Không |
| PUT | /api/v1/epics/{id} | title, description, priority, status |
| DELETE | /api/v1/epics/{id} | Không |
| GET | /api/v1/epics/{id}/progress | Không |
| PATCH | /api/v1/pbis/{id}/epic | `{ "epicId": 1 }` |
| PATCH | /api/v1/tasks/{id}/epic | `{ "epicId": 1 }` |

Title bắt buộc tối đa 255, description tối đa 10000, priority>=1 (số nhỏ ưu tiên cao),
status OPEN/IN_PROGRESS/DONE/CLOSED. Mặc định priority=3,status=OPEN.
POST trả 201, còn lại 200; data Epic gồm epicId,projectId,title,description,priority,status,
createdBy,createdAt,updatedAt. Danh sách data={items,total,page,pageSize}, thứ tự priority rồi epicId.
Không cho ghi vào dự án CLOSED/DELETED. Không có quyền ngầm cho Admin.

Progress data={epicId,totalPbis,completedPbis,pbiProgressPercent,totalTasks,completedTasks,taskProgressPercent}.
Hoàn thành là status DONE, phần trăm theo số lượng, làm tròn 2 chữ số; danh sách rỗng là 0%.
Tách PBI/Task để không cộng đúp công việc; không tự thay đổi status Epic theo tỷ lệ.

Link chỉ chấp nhận Epic cùng dự án. PBI luôn cần Epic theo schema nên endpoint link không nhận null.
Link không chuyển công việc sang dự án khác; trùng liên kết trả 409. Trước khi xóa Epic cần chuyển
hoặc xóa PBI/Task liên kết, nếu còn trả 409 để không xóa mất công việc.
400 dữ liệu sai, 401 chưa xác thực, 403 sai quyền, 404 không có entity trong đúng dự án,
409 xung đột/trạng thái dự án. Mutation và audit cùng transaction, khóa theo dự án.

## Kiểm thử

Theo môi trường độc lập trong `docs/testing-projects.md`:

```powershell
python tests/epics_integration.py
```

Ngày 2026-10-25: 38 kiểm tra thực tế HTTP + SQL đạt. Gồm các vai trò, dữ liệu biên,
CRUD, phân trang/lọc, tiến độ rỗng và có PBI/Task thật, liên kết đồng dự án,
chặn chéo dự án, không xóa Epic còn con, bảo vệ CLOSED/DELETED và audit đúng số lần.
