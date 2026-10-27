# Báo cáo cập nhật dữ liệu frontend và Cloudinary

Ngày 27/10/2026. Dự án BE MyTaiga. Nhánh be/cloudinary-missing-data.
Nguồn yêu cầu: MISSING_DATA_REPORT.md do frontend cung cấp, lưu bản đối chiếu tại
[MISSING_DATA_REQUEST_2026-10-26.md](MISSING_DATA_REQUEST_2026-10-26.md).

## Kết quả và trạng thái bàn giao

Đã cập nhật code cho cả tám nhóm API trong file yêu cầu và chuyển các luồng upload
ảnh mới sang Cloudinary. Giữ nguyên ID/trường cũ, bổ sung dữ liệu nhận diện và alias
để frontend hiện có không bị phá vỡ hợp đồng. Đã có migration SQL, công cụ chuyển
ảnh cũ, hướng dẫn cấu hình và kiểm thử hồi quy.

**Chưa hoàn tất vận hành trên dữ liệu thật:** máy chưa cấu hình tài khoản Cloudinary,
chưa kiểm thử upload cloud thật. Script 16 chưa được áp dụng vào ScrumbanDB thật;
ảnh cũ chưa được tự chuyển lên cloud. Không coi test double là bằng chứng cloud thật.
Trước khi chạy code mới với ScrumbanDB, phải hoàn thành bước nâng schema đã được duyệt.

## Đối chiếu các yêu cầu đã cập nhật

| Nhóm | Dữ liệu và chức năng bổ sung |
|---|---|
| Task và Board | assignee/reporter với tên, avatarCode, avatarUrl, role; Epic title/color; tags; storyPoints; nhãn updatedAtFormatted tính từ thời gian thật |
| Comments | author cùng tên/avatar/role, isEditable theo người đang xem |
| Attachments | originalFileName, uploadedByName, mimeType, fileSizeBytes; URL cloud cho ảnh |
| Notifications | ActorId lưu từ JWT; actor riêng người nhận; tên/avatar/role, targetTitle, isUrgent; alias message/referenceType/referenceId |
| Activity logs | Tên/avatar/role người thao tác, entityTitle, isSystemAction, changes theo trường from/to |
| Chat messages | Sender đầy đủ, alias chatMessageId/createdAt, isEdited/editedAt, replyToMessageId; API sửa tin nhắn của tác giả |
| Chat rooms | Tên/avatar đối phương, trạng thái online theo kết nối thật, participantsCount, lastMessage và tên người gửi gần nhất |
| Bugs | Tên reporter/assignee, taskTitle và taskCode của Task liên kết |

Thông tin gốc không có trong database được trả null hoặc [] tùy trường; không tạo
người, vai trò, màu Epic hoặc lịch sử giả. AvatarCode là chữ viết tắt từ tên thật;
ảnh dùng avatarUrl. Role trong dự án là MANAGER/MEMBER; không tự gán Tech Lead/QA.

## Luồng ảnh Cloudinary

1. Người dùng gửi ảnh từ máy bằng multipart đến backend như trước.
2. Backend kiểm tra JWT, quyền tài nguyên, kích thước và nội dung ảnh.
3. Backend gọi Cloudinary Upload API bằng chữ ký SHA-256 phía server.
4. Database chỉ lưu URL secure_url, không lưu bytes/base64 hoặc local key cho ảnh mới.
5. API trả URL cloud cho frontend sử dụng.

Các nơi đã cập nhật: User.Avatar, ảnh Attachment.FilePath và ảnh MessageAttachment.FilePath.
Giữ tên cột FilePath để tương thích schema cũ, nhưng giá trị ảnh mới là URL HTTPS.
Không thêm bảng lưu ảnh nhị phân hoặc public_id riêng; public_id được suy ra từ URL
Cloudinary đã xác thực khi cần dọn tài nguyên.

Avatar dùng CDN public để hiển thị nhận diện thành viên. Ảnh Task/chat dùng asset
Cloudinary authenticated; API cấp URL cloud có chữ ký hiệu lực 10 phút sau khi kiểm
tra quyền. Frontend phải lấy lại metadata khi link hết hạn. Link đã cấp vẫn có thể
được dùng đến hết hạn; quyền backend không thu hồi tức thì một link cloud đã phát.
Tệp PDF/Office/ZIP/TXT giữ luồng file riêng tư hiện tại, ngoài phạm vi chuyển ảnh.

Upload avatar được giải mã và xuất PNG. Ảnh Task/chat được xác minh nội dung và giới
hạn trước upload. File ảnh tạm được xóa sau khi xử lý; nếu batch/SQL lỗi, backend cố
gắng xóa asset cloud mới. Nếu Cloudinary không sẵn sàng, trả 503 có kiểm soát, không
âm thầm quay về lưu ảnh local. Dọn cloud là best effort: mất mạng khi cleanup có thể
để lại asset mồ côi, cần đối chiếu khi vận hành.

## Schema và dữ liệu cũ

Script [16_FrontendMissingData.sql](../Database/16_FrontendMissingData.sql) bổ sung:

- Task.TagsJson và StoryPoints, kiểm tra JSON và giới hạn điểm.
- Epic.Color.
- Notification.ActorId, ActorRole, TargetTitle.
- ActivityLog.ChangesJson.
- Message.EditedAt và ReplyToMessageId.

Migration không xóa dữ liệu và đã kiểm tra chạy lại trên database thử. Các cột lịch
sử mới là nullable. Notification cũ không biết actor trả actor=null/actorUnknown;
không dùng userId người nhận để giả làm actor. Changes cũ hoặc tác vụ SQL không có
snapshot trả null; chỉ ghi whitelist trường nghiệp vụ, không ghi password/secret.
Tên, role, targetTitle được tra dữ liệu hiện tại khi chưa có snapshot lịch sử.

Admin có POST /api/v1/admin/media/migrate-images để chuyển ảnh cũ theo batch tối đa
20 ảnh mỗi loại. Có dry run, cursor và status từng ảnh; giữ file gốc để sao lưu.
Cập nhật URL có điều kiện để không ghi đè avatar/tệp đã đổi đồng thời. Ảnh mất file
gốc báo SOURCE_MISSING, không tự tạo ảnh thay thế.

**Trạng thái database thật:** kiểm duyệt tự động từ chối áp dụng script 16 vào
ScrumbanDB vì chưa có xác nhận riêng cho việc thay đổi database thật. Lệnh không
được thực thi. Cần chủ dự án cho phép áp dụng migration trước bước này.

## Hợp đồng cho frontend

Chi tiết trường, null semantics và request mẫu nằm trong
[FRONTEND_RESPONSE_CONTRACT.md](FRONTEND_RESPONSE_CONTRACT.md).
OpenAPI đã cập nhật tại [openapi.json](openapi.json).

- PUT Task: bỏ qua tags/storyPoints thì giữ dữ liệu; tags=[] hoặc storyPoints=null để xóa.
- POST/PUT Epic: color dạng #RRGGBB.
- POST chat message: replyToMessageId tùy chọn, phải cùng phòng.
- PUT /api/v1/chat/rooms/{roomId}/messages/{messageId}: chỉ tác giả sửa content.
- Realtime dùng cùng dữ liệu enriched như REST; upsert messageId, không append trùng.
- Online dựa trên kết nối hub và JWT/version hiện tại, không dựa trên tài khoản active.

Presence hiện nằm trong bộ nhớ tiến trình. Nếu chạy nhiều instance, cần backplane và
presence phân tán. Đây là giới hạn triển khai, không được coi là online toàn cụm.

## Kết quả kiểm thử

Build thành công, 0 warning và 0 error. HTTP, SQL, filesystem và WebSocket chạy thật
trên database độc lập MyTaiga_ProjectChecks_20261025. Cloudinary dùng test double
loopback, xác minh chữ ký, lưu bytes trong RAM, trả secure_url, mô phỏng lỗi và destroy.
Không sử dụng tài khoản Cloudinary hoặc SMTP thật trong hồi quy.

**722 kiểm tra đạt qua 18 bộ.** 29 kiểm tra mới trực tiếp đối chiếu missing data/ảnh cloud.

| Bộ kiểm thử | Kết quả | Log |
|---|---|---|
| projects | PASS ALL: 42 checks | [Xem](test-logs/cloud-2026-10-27/projects.txt) |
| members | PASS ALL: 39 checks | [Xem](test-logs/cloud-2026-10-27/members.txt) |
| departments | PASS ALL: 39 checks | [Xem](test-logs/cloud-2026-10-27/departments.txt) |
| epics | PASS ALL: 38 checks | [Xem](test-logs/cloud-2026-10-27/epics.txt) |
| backlog | PASS ALL: 43 checks | [Xem](test-logs/cloud-2026-10-27/backlog.txt) |
| sprints | PASS ALL: 58 checks | [Xem](test-logs/cloud-2026-10-27/sprints.txt) |
| tasks | PASS ALL: 61 checks | [Xem](test-logs/cloud-2026-10-27/tasks.txt) |
| board | PASS ALL: 34 checks | [Xem](test-logs/cloud-2026-10-27/board.txt) |
| wip | PASS ALL: 32 checks | [Xem](test-logs/cloud-2026-10-27/wip.txt) |
| flow | PASS ALL: 61 checks | [Xem](test-logs/cloud-2026-10-27/flow.txt) |
| bugs | PASS ALL: 64 checks | [Xem](test-logs/cloud-2026-10-27/bugs.txt) |
| comments | PASS ALL: 28 checks | [Xem](test-logs/cloud-2026-10-27/comments.txt) |
| attachments | PASS ALL: 33 checks | [Xem](test-logs/cloud-2026-10-27/attachments.txt) |
| notifications | PASS ALL: 23 checks | [Xem](test-logs/cloud-2026-10-27/notifications.txt) |
| chat | PASS ALL: 33 checks | [Xem](test-logs/cloud-2026-10-27/chat.txt) |
| admin | PASS ALL: 29 checks | [Xem](test-logs/cloud-2026-10-27/admin.txt) |
| auth | PASS ALL: 36 checks | [Xem](test-logs/cloud-2026-10-27/auth.txt) |
| missing_data | PASS ALL: 29 checks | [Xem](test-logs/cloud-2026-10-27/missing_data.txt) |

Sau bản chốt xử lý lỗi cloud, đã chạy lại missing_data 29, notifications 23 và auth
36 kiểm tra, đều đạt. Đã tạo database mới MyTaiga_CloudInstallChecks_20261027 từ toàn
bộ script 01–16 theo thứ tự; chạy lại 29 missing_data và 36 tài khoản đều đạt.
Một smoke test riêng xác nhận thiếu Cloudinary config trả 503. Các lượt lặp không
cộng vào tổng. Tổng là các nhãn kiểm tra/assertion nghiệp vụ, không phải số endpoint.

Các tình huống mới bao gồm dữ liệu thật của assignee/author/actor, không nhầm người
nhận với actor, quyền sửa theo người xem, snapshot changes, Cloudinary URL trong DB,
kiểm tra chữ ký và URL cloud, lỗi upload/rollback batch, giữ tags khi client cũ sửa,
20 tags tiếng Việt, chat reply/edit/presence và chuyển avatar cũ có giữ bản sao.

## Các bước còn cần trên tài khoản và database thật

1. Xác nhận cho phép áp dụng script 16 vào ScrumbanDB. Chưa chạy API bản mới với schema cũ.
2. Tạo/đăng nhập Cloudinary Console và chạy scripts/configure-cloudinary.ps1 trên máy.
   Nhập CloudName, ApiKey, ApiSecret vào User Secrets; không gửi Secret qua chat/Git.
3. Khởi động lại API và upload thử một avatar, một ảnh Task, một ảnh chat lên cloud thật.
4. Kiểm tra URL/ảnh trong Cloudinary, database và frontend; sau đó chuyển ảnh cũ qua
   công cụ Admin theo từng batch, xử lý mọi status lỗi hoặc thiếu file nguồn.

Hướng dẫn từng bước: [CLOUDINARY_SETUP.md](CLOUDINARY_SETUP.md).
Chưa tuyên bố các bước thật này hoàn tất. Kiểm thử tải, quota cloud và tích hợp toàn
bộ UI frontend cũng chưa được thay thế bằng test chức năng backend.

## Mã nguồn bàn giao

Nhánh be/cloudinary-missing-data kế thừa be/chapter2-verification. Không tự merge main.
Frontend chỉ cần cập nhật mapping các trường mới; không có thay đổi UI/Figma trong
lượt này. Log kiểm thử, report, hợp đồng response, OpenAPI và cấu hình mẫu đi cùng nhánh.

## Tài liệu Cloudinary đã đối chiếu

- [Upload API và secure_url](https://cloudinary.com/documentation/image_upload_api_reference)
- [Chữ ký xác thực SHA](https://cloudinary.com/documentation/authentication_signatures)
- [Kiểm soát truy cập và URL có thời hạn](https://cloudinary.com/documentation/control_access_to_media)

Kiểm tra OpenAPI cuối: HTTP 200, 93 đường dẫn. Sau sửa schema tags tùy chọn, đã chạy lại Task 61 và missing_data 29 kiểm tra, đều đạt. OpenAPI mô tả route/request; frontend cần đọc hợp đồng bổ sung để map các response enriched.
