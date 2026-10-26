# Báo cáo backend Chương 2 — MyTaiga

Ngày kiểm thử: 26/10/2026. Nguồn yêu cầu: BaoCaoDACN_Nhom8_Chuong2_UCFinalMoi_v4.docx.
Repository: https://github.com/ledinhvuong121/BE-MyTaiga

## Kết quả bàn giao

Backend đã triển khai các nhóm nghiệp vụ của 35 Epic: tài khoản/hồ sơ; dự án/thành viên/
phòng ban; PBI/Epic; Sprint/Burndown; Task/Subtask/Board/WIP; Flow Metrics; Bug;
bình luận/mention/tệp/thông báo; chat riêng/nhóm; quản trị và quyền theo dự án.
Có [bảng đối chiếu từng UC](CHAPTER2_COVERAGE.md), tổng 126 UC, và
[OpenAPI JSON](openapi.json) để import vào Postman. Request/response và quy tắc
của từng nhóm nằm trong các tài liệu testing-* tương ứng.

Các thao tác thu gọn cột, tô màu cột quá tải, vẽ biểu đồ và trang reset mật khẩu
thuộc frontend. Backend cung cấp dữ liệu/cờ/URL tương ứng; chưa kiểm thử toàn bộ
luồng qua giao diện frontend. Cảnh báo deadline và WIP được cung cấp qua API để
frontend hiển thị; chưa triển khai lịch gửi email nhắc deadline.

## Kiểm thử tích hợp cuối

Build thành công, 0 warning, 0 error. Chạy HTTP thật trên Kestrel, SQL Server Express,
đọc/ghi file thật và WebSocket SignalR thật; không mock repository hoặc database.
Database thử: MyTaiga_ProjectChecks_20261025, API 127.0.0.1:5159.
Không dùng tài khoản thật, Gmail SMTP thật hoặc User Secrets thật cho lượt test này.
Thư, avatar và tệp test nằm riêng ở App_Data/test-*.

**693 kiểm tra đạt / 17 bộ kiểm thử, không còn lỗi trong lượt chạy cuối.**

| Bộ | Kết quả | Bằng chứng |
|---|---|---|
| projects | PASS ALL: 42 checks | [Log](test-logs/2026-10-26/projects.txt) |
| members | PASS ALL: 39 checks | [Log](test-logs/2026-10-26/members.txt) |
| departments | PASS ALL: 39 checks | [Log](test-logs/2026-10-26/departments.txt) |
| epics | PASS ALL: 38 checks | [Log](test-logs/2026-10-26/epics.txt) |
| backlog | PASS ALL: 43 checks | [Log](test-logs/2026-10-26/backlog.txt) |
| sprints | PASS ALL: 58 checks | [Log](test-logs/2026-10-26/sprints.txt) |
| tasks | PASS ALL: 61 checks | [Log](test-logs/2026-10-26/tasks.txt) |
| board | PASS ALL: 34 checks | [Log](test-logs/2026-10-26/board.txt) |
| wip | PASS ALL: 32 checks | [Log](test-logs/2026-10-26/wip.txt) |
| flow | PASS ALL: 61 checks | [Log](test-logs/2026-10-26/flow.txt) |
| bugs | PASS ALL: 64 checks | [Log](test-logs/2026-10-26/bugs.txt) |
| comments | PASS ALL: 28 checks | [Log](test-logs/2026-10-26/comments.txt) |
| attachments | PASS ALL: 33 checks | [Log](test-logs/2026-10-26/attachments.txt) |
| notifications | PASS ALL: 23 checks | [Log](test-logs/2026-10-26/notifications.txt) |
| chat | PASS ALL: 33 checks | [Log](test-logs/2026-10-26/chat.txt) |
| admin | PASS ALL: 29 checks | [Log](test-logs/2026-10-26/admin.txt) |
| auth | PASS ALL: 36 checks | [Log](test-logs/2026-10-26/auth.txt) |

Các số trên là nhãn kiểm tra/assertion nghiệp vụ trong script, không phải 693 yêu
cầu độc lập hoặc số lượng endpoint. Một kiểm tra có thể bao gồm nhiều request và
assertion SQL. Không cộng lặp các lượt hồi quy vào tổng.

## Các trường hợp quan trọng đã xác minh

- Đăng ký lưu BCrypt, email trùng, validation; login sai/bộ đếm/tự khóa; profile/avatar.
- Đổi/reset mật khẩu và logout thu hồi JWT; token reset mới thay cũ, hết hạn, dùng
  một lần; hai request reset đồng thời chỉ một request thành công.
- Anonymous/User/Admin/Manager/Member/outsider, quyền đúng dự án; không cho Admin
  đi vòng quyền dự án. Rời dự án mất quyền xem thông báo/chat/tệp.
- Giao dịch rollback và xung đột, request đồng thời, thứ tự PBI/Board, WIP khi di chuyển;
  bảo toàn lịch sử Sprint/Task và công thức Flow Metrics.
- Workflow Bug, tác giả bình luận, mention đúng thành viên, riêng tư tệp, giới hạn
  dung lượng, file giả định dạng, PNG/ZIP/Office, preview/download/range.
- Inbox riêng, read/read-all, read đồng thời; SignalR chỉ phát đến người được phép,
  token version cũ không nhận sự kiện mới. REST khôi phục sau mất kết nối.
- Chat room riêng canonical pair, nhóm do Manager quản lý; file-only message,
  lịch sử theo cursor, tìm kiếm, con trỏ đã đọc không lùi, quyền tải/preview file.
- Admin tạo USER, tìm/lọc người dùng, cấu hình whitelist và giới hạn; batch sai
  không cập nhật một phần. Quy tắc một Admin và Manager kế thừa Member.
- CORS chỉ cho origin frontend đã cấu hình. Query bearer token chỉ được nhận tại hub.

## Cài mới và migration

Đã tạo database mới MyTaiga_InstallChecks_20261026 và chạy thành công lần lượt
01,02,03,07,08,09,10,11,12,13,14,15, không tái sử dụng schema database cũ.
Các migration mới 14 (notification outbox) và 15 (chat delivery/read state) cũng
đã áp dụng vào ScrumbanDB trên máy này; không xóa dữ liệu nghiệp vụ.
Tài liệu cài đặt/cấu hình được viết lại trong [backend/readme.md](../backend/readme.md).
Mỗi máy phải tự cấu hình SQL/JWT/SMTP; Git không mang theo User Secrets hoặc database.

## Nhánh và commit

Các nhánh kế thừa tuần tự. Nhánh bàn giao mới nhất là **be/chapter2-verification**,
chứa cả các chức năng trước. Main giữ nguyên đến khi thực hiện merge theo quy trình nhóm.

| Phần mới ở lượt này | Nhánh | Commit |
|---|---|---|
| Thông báo/nhật ký/SignalR | be/notifications | f08c7c9 |
| Chat/tệp/unread/realtime | be/chat | 28e7b23 |
| Admin/cấu hình/quyền | be/system-admin | c40d4ff |
| Hồi quy tài khoản/avatar/CORS, tài liệu bàn giao | be/chapter2-verification | Xem git log tại nhánh |

## Giới hạn cần giữ khi tích hợp/triển khai

- Kiểm thử chức năng đạt không phải cam kết phần mềm không có lỗi; chưa benchmark tải
  lớn, pentest độc lập hoặc triển khai production/HTTPS công khai.
- SMTP Gmail đã được kiểm thử thủ công trước đó với thư thật; lượt hồi quy này chỉ
  dùng Development để tránh gửi thư ngoài ý muốn. Chưa có queue SMTP/outbox gửi mail
  đảm bảo retry sau sự cố; cần bổ sung khi vận hành production.
- SignalR outbox có thể phát lại sau crash; client phải khử trùng theo ID và tải REST
  khi nối lại. PushedAt là mốc phát, không phải xác nhận người dùng đã nhận/đọc.
- Dữ liệu lịch sử trước các migration không được tự đoán giờ UTC hoặc backfill đầy
  đủ. Flow/Burndown trả metadata thiếu lịch sử; không nên coi dữ liệu cũ là chuỗi hoàn chỉnh.
- Không có tác vụ dọn avatar cũ; xóa file có lỗi I/O có thể để lại file mồ côi riêng tư.
  Kho App_Data và database cần được sao lưu cùng nhau. Kiểm tra file không thay thế antivirus.
- API role giữ đúng một Admin: từ chối cấp Admin thứ hai/gỡ Admin cuối. Chuyển giao
  Admin nguyên tử là luồng mở rộng riêng, không tự thực hiện từ yêu cầu gán vai trò.
- Các phần kiểm thử giao diện và publish API cho máy ở xa vẫn do bước tích hợp/triển khai xử lý.

Kiểm tra cài mới bổ sung: 36 kiểm tra tài khoản + 33 kiểm tra chat = 69 kiểm tra lặp trên database mới đều đạt; không cộng vào tổng 693. Script tái tạo: tests/install-test-database.ps1 (từ chối ghi đè database có sẵn).
