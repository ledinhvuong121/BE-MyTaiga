# Bình luận/mention — UC26

Bearer JWT + thành viên đúng dự án. Project CLOSED chỉ đọc; Sprint CLOSED vẫn trao đổi được.
POST/GET `/api/v1/tasks/{id}/comments`; GET/PUT/DELETE `/api/v1/comments/{id}`.
Body `{ "content":"Nội dung @[Tên hiển thị](user:123)" }`.
Nội dung trim, không trống, tối đa min(MAX_COMMENT_LENGTH,5000); tối đa 20 người được mention.
Frontend chọn người từ danh sách thành viên rồi tạo markup ID; @tên thuần là văn bản,
không suy người nhận qua tên có thể trùng. Mention phải là thành viên hoạt động cùng dự án.
Render nội dung như text/markup an toàn trên frontend, không chèn nguyên nội dung như HTML.

**Chỉ tác giả sửa/xóa**, Manager không được sửa/xóa thay người khác. UserId lấy từ JWT.
List phân trang page>=1,pageSize 1–100, mới nhất trước. Data={items,total,page,pageSize}.
POST 201, mutation 200; item={commentId,taskId,userId,content,mentionedUserIds,createdAt,updatedAt}.
401 token;403 quyền;404 ID/scope;409 project đóng;400 dữ liệu/mention sai.

Create lưu Notification cho assignee và người được mention, loại trùng và chính tác giả.
Edit chỉ thông báo mention mới thêm, không gửi lại người đã có. Người rời dự án hoặc bị khóa
không được nhận thông báo mới. Bình luận/notification/audit cùng transaction.
Không xóa Task hay các comment khác khi xóa một comment. Notification lịch sử có thể trỏ comment đã xóa.

Kiểm thử: `python tests/comments_integration.py` trên môi trường `tests/start-isolated.ps1`.
2026-10-26: 28 kiểm tra HTTP+SQL đạt; quyền tác giả, scope, giới hạn config, mention,
dedup recipients, plain text, paging, mất quyền, đóng/xóa project. Hồi quy Bug detail cũng đạt.
