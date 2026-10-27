# Hợp đồng bổ sung dữ liệu frontend — 27/10/2026

Bổ sung, không xóa các ID và trường cũ. Envelope success/message/data giữ nguyên.
Áp dụng đồng nhất cho REST thành công và payload SignalR message.new / notification.new.
Không thay đổi response ProblemDetails khi validation thất bại.

## Task / Board

Vẫn có assigneeId/reporterId/epicId. Bổ sung:

```typescript
interface PersonSummary {
  userId: number;
  fullName: string;
  avatarCode: string;         // chữ viết tắt từ tên thật, không phải URL
  avatarUrl: string | null;   // URL Cloudinary; null khi chưa có ảnh cloud
  role: 'MANAGER' | 'MEMBER' | 'ADMIN' | 'USER' | null;
}
interface TaskExtra {
  assignee: PersonSummary | null;
  reporter: PersonSummary | null;
  assigneeName: string | null;
  assigneeAvatarCode: string | null;
  assigneeAvatarUrl: string | null;
  assigneeRole: string | null;
  reporterName: string | null;
  reporterAvatarUrl: string | null;
  epic: { epicId: number; title: string; color: string | null } | null;
  epicTitle: string | null;
  epicColor: string | null;
  tags: string[];
  storyPoints: number | null;
  updatedAtFormatted: string;
}
```

POST/PUT Task nhận tags (tối đa 20, mỗi tag 1–50 ký tự) và storyPoints (0–10000/null).
Client cũ không gửi hai trường này khi PUT thì dữ liệu hiện có được giữ.
Gửi tags=[] để xóa tags; gửi storyPoints=null để bỏ điểm. Task cũ trả []/null.
POST/PUT Epic nhận color dạng #RRGGBB; màu chưa thiết lập trả null, không tạo màu giả.
Role là vai trò thật theo dự án, không gán chức danh Tech Lead/QA nếu database không có.
Nên dùng updatedAt ISO để FE tự định dạng theo múi giờ; formatted là nhãn tiếng Việt tính lúc trả API.

## Comments

author: PersonSummary; authorName/authorAvatarCode/authorAvatarUrl/authorRole.
isEditable = userId tác giả bằng user hiện tại. API vẫn là nguồn kiểm tra quyền cuối:
dự án đóng/đã rời dự án có thể cấm sửa dù bản dữ liệu UI cũ từng có isEditable=true.

## Attachments

originalFileName = fileName hiện có: xác nhận đây là tên gốc đã bỏ thành phần đường dẫn,
không phải GUID nội bộ. uploadedByName, mimeType, fileSizeBytes được thêm; fileSize cũng
là byte. Với ảnh, url/previewUrl/downloadUrl là URL cloud có chữ ký. FilePath nội bộ
không được trả ra. Với PDF/Office/ZIP/TXT, giữ endpoint xác thực trên backend.
Ảnh trong chat có cùng originalFileName/mimeType/fileSizeBytes/url/previewUrl/downloadUrl.

## Notifications

userId luôn là NGƯỜI NHẬN, tuyệt đối không dùng làm actor.

```typescript
interface NotificationExtra {
  actorId: number | null;
  actor: (PersonSummary & {
    actorId: number; actorName: string; actorAvatarCode: string; actorRole: string | null;
  }) | null;
  actorName: string | null;
  actorAvatarCode: string | null;
  actorAvatarUrl: string | null;
  actorRole: string | null;
  actorUnknown: boolean;
  targetTitle: string | null;
  isUrgent: boolean;
  message: string | null;        // alias của content
  referenceType: string | null;  // alias của refType
  referenceId: number | null;    // alias của refId
}
```

Thông báo mới lưu ActorId từ JWT đã xác thực, không nhận actor từ body.
Thông báo cũ không có bằng chứng actor trả null/actorUnknown, không đoán từ userId.
System action không có actor cá nhân. Role/tên/title được tra dữ liệu hiện tại,
không tuyên bố đó là snapshot lịch sử tại thời điểm thông báo nếu chưa được lưu.

## Activity logs

Thêm userName/userAvatarCode/userAvatarUrl/userRole/entityTitle/isSystemAction/changes.
changes là object theo trường: {"title":{"from":"Tên cũ","to":"Tên mới"}}.
Chỉ ghi các trường nghiệp vụ whitelist của entity đang được EF theo dõi. Không ghi
PasswordHash/secret/token vào changes. Log cũ hoặc thao tác SQL/procedure không có
snapshot đầy đủ trả changes=null; không suy diễn dữ liệu quá khứ.

## Chat messages / rooms

Messages thêm sender: PersonSummary, senderName/senderAvatarCode/senderAvatarUrl/senderRole,
isEdited/editedAt/replyToMessageId/isEditable. Alias chatMessageId=messageId, createdAt=sentAt.

- POST messages JSON/multipart có replyToMessageId tùy chọn, bắt buộc cùng phòng.
- PUT /api/v1/chat/rooms/{roomId}/messages/{messageId}: {"content":"Nội dung sửa"}.
  Chỉ tác giả được sửa; không đổi replyToMessageId bằng PUT. editedAt cập nhật thực.
- Realtime giữ message.new; cùng messageId có thể là bản cập nhật hoặc phát lại.
  FE cần upsert theo messageId, không append trùng, và lấy REST history khi nối lại.

Rooms thêm otherParticipant + otherParticipantName/AvatarCode/AvatarUrl,
otherParticipantStatus ONLINE/OFFLINE, participantsCount, lastMessage, lastMessageSenderName.
Online dựa trên kết nối SignalR hub chat còn sống và JWT/version hiện tại; không dùng
Status tài khoản=1 làm bằng chứng online. Presence hiện tại nằm trong bộ nhớ tiến trình;
triển khai nhiều instance cần backplane/distributed presence. Quyền phòng vẫn kiểm tra lại.

## URL ảnh

Xem CLOUDINARY_SETUP.md. DB chỉ lưu secure_url chuẩn, không lưu base64/bytes/local key
cho ảnh upload mới. Avatar là URL CDN public; ảnh Task/chat là authenticated asset,
API cấp link cloud ký 10 phút sau khi kiểm tra quyền. FE không cần đặt Authorization
header trên thẻ img dùng URL cloud, nhưng phải gọi API metadata bằng Bearer để lấy link.
Không cache URL ký vĩnh viễn; không đặt ApiSecret lên frontend.
Ảnh cũ phải chuyển bằng công cụ migration sau khi Cloudinary được cấu hình.
