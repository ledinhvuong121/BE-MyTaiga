# Scrumban Board — UC16–UC17

| Method | URL | Quyền/body |
|---|---|---|
| GET | /api/v1/projects/{id}/board?sprintId=&assigneeId=&keyword=&page=1&pageSize=50 | Thành viên |
| PATCH | /api/v1/tasks/{id}/move | Thành viên; `{ "toStatus":"IN_PROGRESS", "newPosition":0 }` |

Bearer JWT bắt buộc. Quyền trong đúng dự án, Admin không bypass. Project CLOSED chỉ đọc;
Task thuộc Sprint CLOSED không di chuyển. Thu gọn/mở rộng cột (UC16.3) do frontend thực hiện
bằng dữ liệu cột trả về; chưa lưu tùy chọn giao diện trên server.

Board data={projectId,columns,page,pageSize}. Column gồm boardColumnId,columnName,position,
wipLimit,totalCount,matchingCount,isOverloaded,hasMore,tasks. Luôn trả cả cột rỗng.
Task card có taskId,title,status,position,priority,progressPercent,assigneeId,sprintId,epicId,
parentTaskId,deadline UTC. pageSize mặc định 50, tối đa 200 **mỗi cột**. totalCount là toàn cột
không chịu filter để thể hiện WIP đúng; matchingCount là số phù hợp filter. Không bỏ qua ID
Sprint/assignee chéo dự án: trả 404. Sắp theo vị trí rồi taskId.

Move dùng vị trí **0-based trong toàn cột**, không phải chỉ các thẻ đang lọc trên UI.
Frontend nên tải thứ tự đầy đủ khi reorder; index đích trong khoảng 0..số thẻ đích trừ chính nó.
Chuyển cột chuẩn hóa vị trí nguồn/đích; reorder cùng cột vẫn cho phép khi cột đang quá tải.
Move giữa cột không được vượt WIP. Khóa project đồng bộ với tạo Task và thay đổi Sprint/member.
Không gọi sp_MoveTask cũ vì API cần giữ chung khóa transaction và các ràng buộc mới.

DONE tự đặt progress=100 và CompletedAt UTC; mở lại xóa CompletedAt, progress 0 nếu trước đó 100,
giữ StartedAt đầu tiên. Task cha chỉ DONE khi Subtask đều DONE; mở lại cha trước khi mở lại con.
Task chưa DONE phải có assignee còn hoạt động nếu đã giao. Không tự giao việc qua move.
Move data={taskId,fromStatus,toStatus,position,progressPercent,startedAt,completedAt}.
Mỗi move thành công ghi TASK_MOVED cùng transaction; lỗi không thay đổi vị trí/status/audit.

400 ID/status/position/filter sai;401 token;403 quyền;404 entity/scope;
409 WIP/phụ thuộc cha-con/Sprint hoặc Project đã đóng.

## Kiểm thử

Dùng database riêng theo `docs/testing-projects.md`, script 10,11 đã áp dụng:

```powershell
python tests/board_integration.py
python tests/tasks_integration.py
```

2026-10-26: 34 kiểm tra Board và 61 kiểm tra hồi quy Task đạt, build sạch.
Gồm các vai trò, Board rỗng/filters/per-column pagination, true WIP counts,
reorder/move, DONE/reopen UTC qua HTTP, ràng buộc cha-con, hai move cạnh tranh một chỗ,
closed Sprint/project và rollback audit. Chưa phải kiểm thử tải lớn hoặc thao tác UI.
