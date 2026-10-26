# Thông báo và nhật ký (UC28)

23 kiểm tra HTTP/SQL/WebSocket đạt ngày 2026-10-26 trên database thử riêng.
Bao gồm inbox riêng, phân trang, unread count, read/read-all, read đồng thời,
ẩn thông báo khi rời dự án, phân quyền nhật ký, thời gian UTC, SignalR thật,
không cho đăng ký nhóm người khác và token bị thu hồi không nhận sự kiện mới.

Chạy script 14 sau script 13. Endpoint /hubs/notifications phát notificationReceived.
Outbox lưu cùng giao dịch nghiệp vụ; PushedAt là mốc thử phát, không phải xác nhận
người dùng đã nhận/đọc. Client tải inbox khi kết nối lại và khử trùng notificationId.
Có thể phát lặp khi tiến trình dừng giữa phát và lưu PushedAt.
