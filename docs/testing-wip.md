# WIP Limit — UC18–UC19

| Method | URL | Quyền/body |
|---|---|---|
| PUT | /api/v1/projects/{id}/board/columns/{columnId}/wip-limit | Manager, `{ "wipLimit":3 }` |
| DELETE | /api/v1/projects/{id}/board/columns/{columnId}/wip-limit | Manager, không giới hạn sau khi xóa |
| GET | /api/v1/projects/{id}/wip | Thành viên |
| GET | /api/v1/projects/{id}/wip/alerts | Thành viên, chỉ các cột quá tải |

Bearer JWT; role xét đúng dự án, Admin không bypass. WIP phải là số nguyên>=1;
bỏ giới hạn bằng DELETE chứ không gửi 0. ID cột phải thuộc đúng project.
Project CLOSED chỉ đọc; DELETED trả 404.

Read data={projectId,columns,asOfUtc}. Column gồm boardColumnId,columnName,position,wipLimit,
taskCount,isOverloaded,atCapacity,excess. Count gồm toàn bộ Task của cột, không phụ thuộc filter UI.
atCapacity là count>=limit; isOverloaded là count>limit; excess=max(0,count-limit).
Không giới hạn trả null, false/false/0 tương ứng.

Manager được hạ limit xuống dưới số hiện tại: không tự di chuyển/xóa công việc,
trả isOverloaded=true. Dùng cờ này để frontend đánh dấu cột quá tải (UC19.4).
Create Task hoặc move từ cột khác vào cột đầy/quá tải bị chặn 409;
move ra khỏi cột và reorder nội bộ vẫn được phép. Mọi mutation giữ khóa theo project.
GET alerts cung cấp dữ liệu cảnh báo (UC19.3); phát SignalR/notification chủ động thuộc module sau.

PUT/DELETE 200, data={boardColumnId,columnName,wipLimit,taskCount,isOverloaded,changed}.
Lặp cùng cấu hình thành công với changed=false, không ghi audit trùng.
400 validation;401 token;403 sai quyền;404 scope/ID không có;409 dự án đã đóng.
WIP_LIMIT_SET/WIP_LIMIT_REMOVED và cấu hình lưu trong cùng transaction.

## Kiểm thử

Dùng môi trường độc lập như `docs/testing-projects.md`:

```powershell
python tests/wip_integration.py
```

2026-10-26: 32 kiểm tra WIP đạt, build sạch. Có kiểm tra qua API Board/Task thực,
không chỉ đọc SQL: các vai trò, dữ liệu biên, scope cột, hạ limit/overload,
atCapacity khác overload, filters không che counts, create/move bị chặn,
move ra/reorder được phép, bỏ limit có hiệu lực, idempotency/audit và CLOSED/DELETED.
