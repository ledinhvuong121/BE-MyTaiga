# Tiến độ backend Chương 2

Cập nhật 2026-10-25. Các nhánh chức năng kế thừa tuần tự, chưa gộp main.
Không đánh đồng entity/schema có sẵn với API đã hoàn thành.

| Phần | Nhánh | Kiểm thử mới |
|---|---|---|
| Tài khoản/xác thực/hồ sơ/Admin lock/Gmail/đăng ký | be/register và các nhánh tổ tiên | Đã kiểm thử thủ công ở các bước trước |
| UC4 dự án | be/projects | 42 kiểm tra HTTP + SQL |
| UC5 thành viên | be/project-members | 39 kiểm tra HTTP + SQL |
| UC6 phòng ban | be/project-departments | 39 kiểm tra HTTP + SQL |
| UC9–UC10 Epic/liên kết/tiến độ | be/epics | 38 kiểm tra HTTP + SQL |
| UC7–UC8 Product Backlog | be/product-backlog | 43 kiểm tra HTTP + SQL |

Nhánh mới nhất chứa đủ các phần trên. Không đổi User Secrets, mật khẩu, email thử,
database ScrumbanDB hoặc file avatar người dùng trong đợt triển khai này.
Database riêng `MyTaiga_ProjectChecks_20261025` được giữ để tra cứu kết quả;
dữ liệu test có tiền tố/tài khoản kiểm thử, không dùng như dữ liệu thật.
Test API chạy ở 127.0.0.1:5159 với cấu hình tiến trình riêng, không dùng Gmail SMTP.

## Còn lại, chưa tuyên bố hoàn thành

- UC11–UC12: Sprint, chọn PBI, tiến độ/Burndown, đóng Sprint.
- UC13–UC17: Task/Subtask/phân công/deadline/priority, Board và di chuyển/sắp xếp.
- UC18–UC23: cấu hình/cảnh báo WIP, Flow Metrics.
- Các nhóm Bug, bình luận/tệp, thông báo/SignalR, chat,
  thống kê/báo cáo, quản trị hệ thống và các UC còn lại trong báo cáo.
- Kiểm thử tải lớn, rà soát toàn bộ bảo mật và bàn giao tích hợp frontend
  là công việc riêng, không được thay bằng các test chức năng này.

Tiếp theo: Sprint. Lưu ý scaffold hiện có `Project.Sprint` số ít do unique filtered
index một Sprint ACTIVE; cần kiểm tra mapping kỹ khi triển khai nhiều Sprint theo thời gian.

## Cách tiếp tục

1. Repo sạch, tạo nhánh mới từ nhánh chức năng mới nhất.
2. Đối chiếu UC, schema, trigger/procedure và phân quyền đúng dự án.
3. Triển khai API, viết tài liệu request/response và hành vi lỗi.
4. Build, kiểm thử HTTP + SQL trên database độc lập, sửa mọi test lỗi.
5. Commit và push nhánh đã đạt, cập nhật bảng này rồi mới chuyển nhóm tiếp theo.

Các script test và hướng dẫn chi tiết nằm trong `tests/` và `docs/testing-*.md`.
Kết quả kiểm thử là bằng chứng cho các trường hợp đã chạy, không phải cam kết không còn lỗi.
