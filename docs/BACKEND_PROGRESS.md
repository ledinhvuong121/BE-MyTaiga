# Tiến độ backend Chương 2

Cập nhật 2026-10-26. Các nhánh chức năng kế thừa tuần tự, chưa gộp main.
Không đánh đồng entity/schema có sẵn với API đã hoàn thành.

| Phần | Nhánh | Kiểm thử mới |
|---|---|---|
| Tài khoản/xác thực/hồ sơ/Admin lock/Gmail/đăng ký | be/register và các nhánh tổ tiên | Đã kiểm thử thủ công ở các bước trước |
| UC4 dự án | be/projects | 42 kiểm tra HTTP + SQL |
| UC5 thành viên | be/project-members | 39 kiểm tra HTTP + SQL |
| UC6 phòng ban | be/project-departments | 39 kiểm tra HTTP + SQL |
| UC9–UC10 Epic/liên kết/tiến độ | be/epics | 38 kiểm tra HTTP + SQL |
| UC7–UC8 Product Backlog | be/product-backlog | 43 kiểm tra HTTP + SQL |
| UC11–UC12 Sprint/Burndown/đóng Sprint | be/sprints | 58 kiểm tra HTTP + SQL, 201 kiểm tra hồi quy đạt |
| UC13–UC15 Task/Subtask/phân công/deadline/priority | be/tasks | 61 kiểm tra HTTP + SQL |
| UC16–UC17 Board/move/lọc/tìm | be/board | 34 kiểm tra HTTP + SQL; UC16.3 giao diện frontend |
| UC18–UC19 WIP Limit/cảnh báo | be/wip-limits | 32 kiểm tra HTTP + SQL; UC19.4 hiển thị bởi frontend |
| UC20–UC23 Flow Metrics | be/flow-metrics | 61 kiểm tra HTTP + SQL, 386 hồi quy đạt |
| UC24–UC25 Bug | be/bugs | 64 kiểm tra HTTP + SQL |

Nhánh mới nhất chứa đủ các phần trên. Không đổi User Secrets, mật khẩu, email thử
hoặc file avatar người dùng. Sprint bổ sung schema qua script 10 như ghi bên dưới.
Database riêng `MyTaiga_ProjectChecks_20261025` được giữ để tra cứu kết quả;
dữ liệu test có tiền tố/tài khoản kiểm thử, không dùng như dữ liệu thật.
Test API chạy ở 127.0.0.1:5159 với cấu hình tiến trình riêng, không dùng Gmail SMTP.

## Còn lại, chưa tuyên bố hoàn thành

- Các nhóm bình luận/tệp, thông báo/SignalR, chat,
  thống kê/báo cáo, quản trị hệ thống và các UC còn lại trong báo cáo.
- Kiểm thử tải lớn, rà soát toàn bộ bảo mật và bàn giao tích hợp frontend
  là công việc riêng, không được thay bằng các test chức năng này.

Tiếp theo: bình luận/tệp/thông báo/chat và quản trị, rà soát toàn bộ Chương 2.
Flow Metrics cần script 12 sau 11; mốc lịch sử trước nâng cấp có thể thiếu và được đánh dấu trong API.
Mapping Project–Sprint đã sửa thành một-nhiều; cần script 10 cho các tính năng Sprint mới.
Ngày 2026-10-26 đã áp dụng script 10 bổ sung schema (không xóa dữ liệu) vào ScrumbanDB;
database thử vẫn riêng và các kiểm thử không chạy trên dữ liệu người dùng.

## Cách tiếp tục

1. Repo sạch, tạo nhánh mới từ nhánh chức năng mới nhất.
2. Đối chiếu UC, schema, trigger/procedure và phân quyền đúng dự án.
3. Triển khai API, viết tài liệu request/response và hành vi lỗi.
4. Build, kiểm thử HTTP + SQL trên database độc lập, sửa mọi test lỗi.
5. Commit và push nhánh đã đạt, cập nhật bảng này rồi mới chuyển nhóm tiếp theo.

Các script test và hướng dẫn chi tiết nằm trong `tests/` và `docs/testing-*.md`.
Kết quả kiểm thử là bằng chứng cho các trường hợp đã chạy, không phải cam kết không còn lỗi.
