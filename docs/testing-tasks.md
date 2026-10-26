# Task — UC13–UC15

Chạy script **Database/11_TaskUtcHistory.sql** sau 10. Script thay trigger Task để dùng UTC,
giữ thời điểm bắt đầu đầu tiên qua các lần làm lại và cho phép bảo toàn người tạo đã rời dự án.
Không sửa người tạo cũ hoặc đoán/chuyển múi giờ của dữ liệu lịch sử trước nâng cấp.

| Method | URL | Quyền / body |
|---|---|---|
| GET | /api/v1/projects/{id}/tasks?page=1&pageSize=20&sprintId=&assigneeId=&keyword=&status= | Thành viên |
| POST | /api/v1/projects/{id}/tasks | Thành viên, body tạo bên dưới |
| GET | /api/v1/tasks/{id} | Thành viên, trả task và subtasks |
| PUT | /api/v1/tasks/{id} | Thành viên; `{ "title":"Tên mới", "description":null }` |
| DELETE | /api/v1/tasks/{id} | Thành viên; chặn nếu còn con/Bug/comment/file |
| POST | /api/v1/tasks/{id}/subtasks | Manager; body tạo, kế thừa Sprint/Epic cha |
| PATCH | /api/v1/tasks/{id}/assignee | Manager; `{ "assigneeId":3 }`, null để bỏ giao |
| PATCH | /api/v1/tasks/{id}/priority | Manager; `{ "priority":"HIGH" }` |
| PATCH | /api/v1/tasks/{id}/deadline | Manager; `{ "deadline":"2026-12-01T10:00:00+07:00" }`, null để bỏ |
| PATCH | /api/v1/tasks/{id}/progress | Thành viên; `{ "progressPercent":55 }` |
| GET | /api/v1/projects/{id}/tasks/overdue?page=1&pageSize=20 | Manager |
| GET | /api/v1/projects/{id}/tasks/deadline-alerts?page=1&pageSize=20 | Manager, OVERDUE/DUE_SOON |

```json
{
  "title":"Làm API",
  "description":"Mô tả",
  "sprintId":1,
  "epicId":1,
  "parentTaskId":null,
  "assigneeId":null,
  "priority":"MEDIUM",
  "deadline":null
}
```

Member chỉ tạo Task thường; chỉ Manager được cung cấp parent/assignee/deadline hoặc priority
khác mặc định MEDIUM. Manager kế thừa mọi quyền TASK_CRUD của Member, không cần gán hai role.
Mọi quyền xét trong dự án; Admin không bypass. ReporterId lấy từ JWT, Status luôn TODO, progress=0.
Title trim bắt buộc<=255, description<=10000; priority LOW/MEDIUM/HIGH/CRITICAL.
Sprint/Epic/cha phải cùng dự án; Sprint phải chưa CLOSED. Subtask kế thừa scope cha,
không tạo dưới Task DONE, tối đa 32 cấp và không hỗ trợ đổi cha tùy tiện.
Assignee phải là thành viên hoạt động. Không cho Member lách quyền qua body tạo.

Task trong Sprint CLOSED hoặc dự án CLOSED không ghi; DELETED không đọc.
Tiến độ nhập 0–99; DONE do Board cập nhật thành 100, khi mở lại phải đổi trạng thái trên Board.
Không xóa dây chuyền dữ liệu liên quan. CRUD Member áp dụng toàn dự án theo TASK_CRUD của seed,
không giới hạn chỉ Task do mình tạo. Timestamp mới trả UTC, deadline chuyển offset sang UTC.

GET Task: data={task,subtasks}; POST trả 201 với data Task trực tiếp, Location header;
mutation khác 200, DELETE data=null. Task gồm taskId,projectId,sprintId,epicId,parentTaskId,
assigneeId,reporterId,title,description,priority,status,deadline,progressPercent,position,
startedAt,completedAt,createdAt,updatedAt. List data={items,total,page,pageSize}.
401 token;403 quyền;404 entity/scope;409 trạng thái/assignee/dependency/WIP;400 validation.

Deadline alerts trả items={task,alert}, total,page,pageSize,warningHours,asOfUtc.
Khoảng DUE_SOON dùng DEADLINE_WARNING_HOURS; Task DONE được loại. Đây là API dữ liệu cảnh báo,
chưa có background job phát thông báo theo giờ. Phân công lưu Notification cùng transaction;
SignalR sẽ thuộc module thông báo. Đặt WIP và kéo Task thuộc phần Board tiếp theo;
tạo TODO hiện đã kiểm tra WIP và khóa dự án để chống vượt giới hạn đồng thời.

## Kiểm thử

Theo database độc lập trong `docs/testing-projects.md`, áp dụng cả script 10,11:

```powershell
python tests/tasks_integration.py
```

2026-10-26: 61 kiểm tra HTTP+SQL đạt, build sạch. Gồm các vai trò, validation, CRUD/Subtask,
scope/assignee, offset deadline, lọc/tìm, tiến độ, lịch sử UTC khi làm lại,
người tạo rời dự án, dependency delete, Sprint/project CLOSED, mất quyền,
thông báo và hai request tạo cạnh tranh WIP.
Kiểm tra trạng thái tại lớp trigger dùng SQL fixture, chưa thay cho kiểm thử Board API.
