# Sprint — UC11.1–UC11.4, UC12.1–UC12.4

## Cài đặt

Sau script 09, chạy **Database/10_SprintBurndownEvents.sql** trên đúng ScrumbanDB.
Script chạy trong transaction, có thể chạy lại; thêm bảng lịch sử, trigger theo dõi thay đổi
PBI/SprintBacklogItem và hai cột snapshot khi đóng Sprint. Không xóa dữ liệu cũ.
EF đã sửa Project–Sprint thành một-nhiều; unique filtered index vẫn giữ tối đa một Sprint ACTIVE.
Máy đồng đội cần pull đúng nhánh và chạy script 10 trước khi dùng Sprint/Backlog với bản mới.

## API

Tất cả cần Bearer JWT. Thành viên được đọc, chỉ Manager đúng dự án được ghi.
Admin không có quyền ngầm trong dự án. Các dự án CLOSED chỉ đọc; DELETED trả 404.

| Method | URL | Dữ liệu |
|---|---|---|
| GET | /api/v1/sprints?projectId=1&status=PLANNED&page=1&pageSize=20 | Danh sách phân trang |
| POST | /api/v1/sprints | Body bên dưới, trả 201 PLANNED |
| GET | /api/v1/sprints/{id} | Chi tiết, có Sprint Goal |
| PUT | /api/v1/sprints/{id} | Cùng body POST |
| DELETE | /api/v1/sprints/{id} | Chỉ PLANNED chưa có Task/FlowMetric |
| POST | /api/v1/sprints/{id}/start | PLANNED -> ACTIVE, phải có PBI |
| POST | /api/v1/sprints/{id}/close | ACTIVE -> CLOSED |
| POST | /api/v1/sprints/{id}/backlog-items | `{ "pbiIds": [1,2] }` |
| DELETE | /api/v1/sprints/{id}/backlog-items/{pbiId} | Chỉ PLANNED |
| GET | /api/v1/sprints/{id}/backlog-items | PBI và tiến độ |
| GET | /api/v1/sprints/{id}/progress | PBI, Story Point, tiến độ và số Task |
| GET | /api/v1/sprints/{id}/burndown | Dữ liệu biểu đồ theo ngày UTC |

```json
{
  "projectId": 1,
  "sprintName": "Sprint 1",
  "sprintGoal": "Hoàn thiện xác thực",
  "startDate": "2026-10-26",
  "endDate": "2026-11-08"
}
```

Tên bắt buộc <=150, duy nhất trong dự án; Goal tùy chọn <=500.
Ngày bắt buộc, EndDate>=StartDate, tối đa 366 ngày; không nhận ngày cực đại làm tràn timeline.
Không chồng lấn Sprint chưa CLOSED (hai khoảng chạm cùng ngày cũng chồng lấn).
Không chuyển Sprint sang dự án khác; không đổi lịch khi ACTIVE; CLOSED không sửa/xóa/mở lại.
POST không cho chọn status từ body; start/close là thao tác riêng.

Chọn PBI nhận 1–500 ID, không trùng, tất cả cùng dự án, chưa DONE và chưa ở Sprint mở khác.
Toàn bộ batch thành công hoặc rollback. PBI chuyển IN_SPRINT; loại khỏi PLANNED về READY.
Đóng Sprint lưu snapshot điểm/trạng thái PBI, giữ liên kết lịch sử và trả PBI chưa xong về READY.
PBI chưa xong có thể chọn lại cho Sprint sau. Progress Sprint cũ dùng snapshot, không đổi theo
ước lượng/trạng thái tương lai của PBI. Task vẫn giữ liên kết Sprint để bảo toàn lịch sử.
Xóa Sprint PLANNED trả PBI về READY; Task/FlowMetric hiện có sẽ chặn xóa, không cascade mất công việc.

Response theo envelope success/message/data. List data={items,total,page,pageSize}.
Chi tiết Sprint có sprintId,projectId,sprintName,sprintGoal,startDate,endDate,status.
Progress data có sprint,items,totalPbis,completedPbis,totalStoryPoints,completedStoryPoints,
remainingStoryPoints,progressPercent,totalTasks,completedTasks. Tỷ lệ theo Story Point; tổng 0 trả 0%.
400 dữ liệu sai; 401 token; 403 quyền; 404 entity/scope; 409 trùng tên/lịch/trạng thái/ràng buộc.

## Burndown

SQL ghi delta scope/remaining khi chọn hoặc loại PBI, khi PBI đổi trạng thái/Story Point.
Không suy diễn lịch sử từ trạng thái hiện tại. CLOSED ngừng ghi thay đổi tương lai của PBI.
Response: sprintId,unit=storyPoints,totalScope,historyAvailableFrom,points,note.
Mỗi point có date,idealRemaining,actualRemaining; startDate đến endDate+1 (điểm lý tưởng cuối=0).
Actual là tổng delta đến cuối ngày UTC; tương lai hoặc trước baseline migration là null.
Ideal là đường thẳng theo **scope hiện tại**, có thể thay đổi khi thêm/bớt/đổi ước lượng.
Với dữ liệu cũ, migration chỉ ghi baseline tại lúc nâng cấp, không có khả năng khôi phục lịch sử trước đó.

Tạo/đóng Sprint ghi ActivityLog và tạo Notification cho các thành viên hoạt động khác người thao tác
trong cùng transaction. Đẩy SignalR tức thời thuộc phần thông báo chưa triển khai.

## Kiểm thử thực tế

Dùng môi trường tách biệt trong `docs/testing-projects.md`, chạy script 10 trên bản sao database thử trước.

```powershell
python tests/sprints_integration.py
```

2026-10-26: **58 kiểm tra đạt**, build 0 warning/0 error.
201 kiểm tra hồi quy project/member/department/epic/backlog cũng đạt trên schema mới.
Kiểm tra phân quyền, dữ liệu biên, lịch trùng, nhiều Sprint cùng dự án,
hai request start đồng thời, chọn PBI batch rollback/chéo dự án/trùng Sprint,
tiến độ, Burndown hoàn thành/mở lại/đổi điểm, snapshot đóng và carry-over,
xóa PLANNED/Task/metrics, thông báo, ngày cực đại và baseline chưa có lịch sử.
Script migration được chạy lặp trên database thử, không thêm bản ghi baseline trùng.
