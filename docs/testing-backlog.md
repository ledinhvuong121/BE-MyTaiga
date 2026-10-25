# Product Backlog (UC7.1–UC7.4, UC8.1–UC8.4)

Bearer JWT. Thành viên đọc; Manager dự án ghi. Admin không có quyền dự án ngầm.

| Method | URL | Body |
|---|---|---|
| GET | /api/v1/product-backlog-items?projectId=1&epicId=&status=&keyword=&page=1&pageSize=20 | Không |
| POST | /api/v1/product-backlog-items | epicId,title,description,storyPoint,priority,status |
| GET | /api/v1/product-backlog-items/{id} | Không |
| PUT | /api/v1/product-backlog-items/{id} | Cùng body POST, thay thế trường |
| DELETE | /api/v1/product-backlog-items/{id} | Không |
| PATCH | /api/v1/product-backlog-items/{id}/priority | `{ "priority": 1 }` |
| PUT | /api/v1/product-backlog-items/order | `{ "epicId": 1, "pbiIds": [3,1,2] }` |
| PATCH | /api/v1/product-backlog-items/{id}/epic | `{ "epicId": 2 }`, alias của API liên kết Epic |

PBI bắt buộc thuộc Epic theo schema, project được suy ra từ Epic.
Title bắt buộc <=255, description<=10000, storyPoint>=0, priority>=1;
status NEW/READY/IN_SPRINT/DONE (mặc định NEW), priority mặc định 3.
Không cho gán Epic của dự án khác khi cập nhật; không đổi scope dự án qua request.

POST trả 201, các thao tác khác 200; chi tiết data gồm pbiId,epicId,title,description,
storyPoint,priority,status,createdAt,updatedAt. Danh sách data={items,total,page,pageSize},
thứ tự priority ASC,pbiId ASC, pageSize 1–100. projectId bắt buộc khi lấy danh sách.

Schema hiện dùng Priority cho cả ưu tiên/thứ tự: số nhỏ đứng trước. API order nhận **toàn bộ PBI
của một Epic**, mỗi ID đúng một lần, tối đa 1000 ID; gán Priority=1..N atomically. Nếu thiếu/thừa/
ID khác Epic trả 409, cần tải lại danh sách, không cập nhật dở dang.

Không thể tạo IN_SPRINT khi chưa có SprintBacklogItem; không đổi PBI còn liên kết Sprint về
NEW/READY hoặc xóa khi vẫn liên kết. Cần thao tác Sprint riêng trước. Dự án CLOSED chỉ đọc,
DELETED không trả dữ liệu. Không tự xóa Sprint/Task vì thao tác PBI.

400 dữ liệu sai; 401 token; 403 quyền; 404 entity/scope sai; 409 trạng thái/ràng buộc Sprint/order.
Mutation và audit cùng transaction, tuần tự hóa theo dự án.

## Kiểm thử

Theo môi trường trong `docs/testing-projects.md`:

```powershell
python tests/backlog_integration.py
python tests/epics_integration.py
```

2026-10-25: 43 kiểm tra Backlog đạt, 38 kiểm tra Epic hồi quy đạt.
Đã bắt và sửa lỗi thiếu projectId trả 404 thay vì 400 trong lúc kiểm thử.
Bao gồm các vai trò, validation, CRUD, lọc/tìm/phân trang, chặn chuyển chéo dự án,
priority/order và kết quả SQL, danh sách order sai không cập nhật, liên kết Sprint,
bảo vệ CLOSED/DELETED và audit chính xác.
