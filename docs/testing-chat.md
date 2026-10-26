# Chat UC29–UC32

33 kiểm tra HTTP/SQL/WebSocket thật đạt. Phòng riêng chỉ cho người cùng dự án;
canonical pair chống trùng; nhóm do Manager tạo/quản lý, thành viên phải thuộc dự án.
Rời dự án hoặc bị bỏ khỏi phòng mất quyền xem, tải tệp và nhận sự kiện.
Bỏ khỏi phòng giữ bản ghi người gửi để bảo toàn lịch sử/FK; thêm lại kích hoạt lại.

API: /api/v1/chat/rooms (GET/POST), /rooms/{id}/participants (GET/POST),
/rooms/{id}/participants/{userId} (DELETE), /rooms/{id}/messages (GET/POST),
/rooms/{id}/read (POST), /unread-count (GET), /attachments/{id} và /preview (GET).
JSON gửi {"content":"Xin chào"}; multipart content + files. Lịch sử before/limit/keyword.
Read nhận {"messageId":123} hoặc {} đọc đến tin hiện tại. Con trỏ tăng đơn điệu.
SignalR /hubs/chat event message.new. Khôi phục lịch sử từ REST khi nối lại;
outbox có thể phát lặp, frontend khử trùng theo messageId. Không có delivery receipt.
Tệp dùng cùng bộ xác minh và kho riêng tư như UC27, không công khai đường dẫn đĩa.
Chạy Database/15_ChatDelivery.sql sau 14 trước khi khởi động phiên bản này.

Sự kiện notification.new được phát trên hub chat, đã xác minh bằng WebSocket thật.
