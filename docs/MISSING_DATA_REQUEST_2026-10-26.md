# Báo cáo Missing Data — API cần bổ sung

> **Ngày lập**: 26/10/2026 | **Phiên bản FE**: All 8 Slices tích hợp xong
> **Nguyên tắc**: FE giữ nguyên UI Figma, hiển thị fallback `Thành viên #userId` / `Chưa giao` thay vì ẩn. Khi BE bổ sung trường, chỉ cần xóa fallback, không cần sửa layout.

---

## 1. Tasks & Kanban Board — `GET /api/v1/projects/{id}/board` & `GET /api/v1/tasks/{id}`

### Interface hiện tại (BE)

```typescript
// BoardTaskCard — dữ liệu thẻ trên Kanban
interface BoardTaskCard {
  taskId, title, status, position, priority, progressPercent,
  assigneeId: number | null,   // ⚠️ chỉ có ID
  sprintId, epicId, parentTaskId, deadline
}

// TaskData — chi tiết task
interface TaskData {
  taskId, projectId, sprintId, epicId, parentTaskId,
  assigneeId: number | null,   // ⚠️ chỉ có ID
  reporterId: number,          // ⚠️ chỉ có ID
  title, description, priority, status, deadline,
  progressPercent, position, startedAt, completedAt, createdAt, updatedAt
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị (FE) | Fallback hiện tại |
|---|---|---|---|
| `assigneeName` | `string` | Tên assignee trên thẻ Kanban & Drawer | `"Thành viên #${assigneeId}"` |
| `assigneeAvatarCode` | `string` | Avatar code trên thẻ Kanban & Drawer | `"U${assigneeId}"` |
| `assigneeRole` | `string` | Role hiển thị trong Drawer | `"Thực hiện"` (hardcoded) |
| `reporterName` | `string` | Người tạo task trong Drawer | Không hiển thị |
| `epicTitle` | `string \| null` | Epic badge trên thẻ Kanban | Không hiển thị |
| `epicColor` | `string \| null` | Màu Epic badge | Màu mặc định |
| `tags` | `string[]` | Tags/label trên thẻ Kanban | Không hiển thị |
| `storyPoints` | `number \| null` | Story points trên thẻ | Không hiển thị |
| `updatedAtFormatted` | `string` | "Cập nhật 42 phút trước" trong Drawer | `"Cập nhật 42 phút trước"` (hardcoded) |

**Đề xuất bổ sung:**
```typescript
assignee?: { userId: number; fullName: string; avatarCode: string; role?: string } | null
reporter?: { userId: number; fullName: string; avatarCode: string } | null
epic?: { epicId: number; title: string; color?: string } | null
tags?: string[]
storyPoints?: number | null
```

---

## 2. Comments — `GET /api/v1/tasks/{id}/comments`

### Interface hiện tại (BE)

```typescript
interface CommentData {
  commentId, taskId,
  userId: number,    // ⚠️ chỉ có ID, không có tên
  content,
  mentionedUserIds: number[],
  createdAt, updatedAt
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `authorName` | `string` | Tên người bình luận trong Drawer & Bug Detail | `"Thành viên #${userId}"` |
| `authorAvatarCode` | `string` | Avatar 2 ký tự | `"U${userId}"` |
| `authorRole` | `string` | Role người bình luận | `"Thành viên"` |
| `isEditable` | `boolean` | Hiện nút Edit/Xóa chỉ cho comment của mình | Chưa phân biệt |

**Đề xuất bổ sung:**
```typescript
author: { userId: number; fullName: string; avatarCode: string; role?: string }
isEditable?: boolean  // true nếu userId == current user
```

---

## 3. Attachments — `GET /api/v1/tasks/{id}/attachments`

### Interface hiện tại (BE)

```typescript
interface AttachmentData {
  attachmentId, taskId,
  fileName: string,         // ⚠️ tên file nội bộ (có thể là UUID/hash)
  fileSize: number,
  uploadedBy: number,       // ⚠️ chỉ có userId
  uploadedAt: string,
  previewUrl: string,
  downloadUrl: string
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `originalFileName` | `string` | Tên file gốc hiển thị cho user | Dùng `fileName` (có thể là UUID) |
| `uploadedByName` | `string` | Tên người tải lên | `"Thành viên #${uploadedBy}"` |
| `mimeType` | `string` | Icon phân loại file (PDF/image/code) | Đoán từ đuôi file |
| `fileSizeBytes` | `number` | Hiển thị "1.2 MB" | `fileSize` (chưa rõ đơn vị) |

> [!WARNING]
> `fileName` của BE có thể là tên file nội bộ (hash/UUID). FE đang dùng làm tên hiển thị.
> Cần xác nhận BE có lưu `originalFileName` riêng không, hoặc `fileName` chính là tên gốc.

---

## 4. Notifications — `GET /api/v1/notifications`

### Interface hiện tại (BE)

```typescript
interface NotificationData {
  notificationId,
  userId: number,           // ⚠️ người NHẬN (không phải actor)
  title, message, type,
  referenceType, referenceId,
  isRead, createdAt, readAt
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `actorId` | `number` | userId người GÂY RA thông báo | Không có — đang dùng sai `userId` |
| `actorName` | `string` | Tên actor trong Bell dropdown | `"Thành viên #${userId}"` (sai) |
| `actorAvatarCode` | `string` | Avatar actor | `"U${userId}"` (sai) |
| `actorRole` | `string` | Role actor (Manager, System...) | `"Thành viên"` |
| `targetTitle` | `string` | Tiêu đề entity liên quan | Không hiển thị |
| `isUrgent` | `boolean` | Badge urgent/WIP alert | Đoán từ `type.includes('WIP')` |

> [!CAUTION]
> **Lỗi ngữ nghĩa nghiêm trọng**: `userId` trong response là ID người NHẬN thông báo.
> FE đang render `"Thành viên #${n.userId}"` như là **tên người gây ra** — hoàn toàn sai.
> BE cần thêm `actor: { actorId, actorName, actorAvatarCode }` riêng biệt.

---

## 5. Activity Logs — `GET /api/v1/projects/{id}/activity-logs`

### Interface hiện tại (BE)

```typescript
interface ActivityLogItem {
  activityLogId,
  userId: number,         // ⚠️ chỉ có ID
  action: string,         // e.g. "TASK_MOVED"
  entityType: string,     // e.g. "TASK", "BUG"
  entityId: number | null,
  description: string,
  createdAt: string
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `userName` | `string` | Tên người thực hiện trong ActivityTimeline | `"Thành viên #${userId}"` |
| `userAvatarCode` | `string` | Avatar người thực hiện | `"U${userId}"` |
| `userRole` | `string` | Role (Manager, Dev, QA...) | `"Thành viên"` |
| `entityTitle` | `string` | Tiêu đề entity (Task title, Bug title) | Không hiển thị |
| `isSystemAction` | `boolean` | Phân biệt hành động Bot/System | `false` (hardcoded) |
| `changes` | `object` | Chi tiết thay đổi (from status A → B) | Không hiển thị |

---

## 6. Chat Messages — `GET /api/v1/chat/rooms/{id}/messages`

### Interface hiện tại (BE)

```typescript
interface ChatMessageData {
  chatMessageId, chatRoomId,
  senderId: number,       // ⚠️ chỉ có ID
  content: string | null,
  createdAt: string,
  attachments?: ChatAttachmentData[]
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `senderName` | `string` | Tên người gửi trong chat bubble | `"Thành viên #${senderId}"` |
| `senderAvatarCode` | `string` | Avatar 2 ký tự | `"U${senderId}"` |
| `senderRole` | `string` | Role người gửi | `"Thành viên"` |
| `isEdited` | `boolean` | Badge "đã chỉnh sửa" | Không hiển thị |
| `editedAt` | `string \| null` | Thời điểm chỉnh sửa | Không hiển thị |
| `replyToMessageId` | `number \| null` | Thread/reply message | Không có |

---

## 7. Chat Rooms — `GET /api/v1/chat/rooms`

### Interface hiện tại (BE)

```typescript
interface ChatRoomData {
  chatRoomId,
  roomType: 'PRIVATE' | 'GROUP',
  roomName: string | null,   // ⚠️ null với PRIVATE room
  privateUserLowId, privateUserHighId,
  lastMessage?: ChatMessageData | null,
  unreadCount?: number
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `otherParticipantName` | `string` | Tên đối phương trong phòng PRIVATE | `"Trò chuyện riêng #${chatRoomId}"` |
| `otherParticipantAvatarCode` | `string` | Avatar đối phương | `"DM"` (hardcoded) |
| `otherParticipantStatus` | `string` | Online/Offline badge | Không hiển thị |
| `participantsCount` | `number` | Số thành viên kênh GROUP | Không hiển thị |
| `lastMessageSenderName` | `string` | "Minh Quân: ..." trong preview | Không có tên |

---

## 8. Bugs — `GET /api/v1/projects/{id}/bugs`

### Interface hiện tại (BE)

```typescript
interface BugData {
  bugId, taskId,
  reporterId: number,        // ⚠️ chỉ có ID
  assigneeId: number | null, // ⚠️ chỉ có ID
  title, description, environment,
  severity, priority, status,
  createdAt, updatedAt, resolvedAt
}
```

### Trường FE cần mà BE chưa trả

| Trường cần | Kiểu | Vị trí hiển thị | Fallback hiện tại |
|---|---|---|---|
| `reporterName` | `string` | Người báo lỗi trong Bug list & Detail | `"User #${reporterId}"` |
| `assigneeName` | `string` | Người nhận sửa lỗi | `"Thành viên #${assigneeId}"` |
| `taskTitle` | `string \| null` | Tiêu đề Task liên kết | Không hiển thị |
| `taskCode` | `string \| null` | Mã Task (e.g. `#TASK-241`) | Không hiển thị |

---

## 9. Tóm tắt theo mức độ ưu tiên

### 🔴 Cao — Ảnh hưởng trực tiếp đến UX nhận diện người dùng

| # | API Endpoint | Trường cần thêm | Lý do |
|---|---|---|---|
| 1 | `GET /board`, `GET /tasks/{id}` | `assignee.fullName`, `assignee.avatarCode` | Thẻ Kanban hiện `Thành viên #5` thay vì tên thật |
| 2 | `GET /tasks/{id}/comments` | `author.fullName`, `author.avatarCode` | Drawer hiện `Thành viên #3` |
| 3 | `GET /notifications` | `actor.actorId`, `actor.actorName` | Đang map sai `userId` (người nhận ≠ người gây ra) |
| 4 | `GET /chat/rooms/{id}/messages` | `senderName`, `senderAvatarCode` | Chat bubble hiện `Thành viên #2` |
| 5 | `GET /chat/rooms` | `otherParticipantName` | Phòng DM không có tên đối phương |

### 🟡 Trung bình — UI có placeholder nhưng không crash

| # | API Endpoint | Trường cần thêm | Lý do |
|---|---|---|---|
| 6 | `GET /board` | `epicTitle`, `epicColor`, `tags` | Epic badge và Tags không hiển thị |
| 7 | `GET /projects/{id}/bugs` | `reporterName`, `assigneeName`, `taskCode` | Bug list thiếu tên người |
| 8 | `GET /projects/{id}/activity-logs` | `userName`, `entityTitle` | Timeline thiếu tên và chi tiết |
| 9 | `GET /tasks/{id}/attachments` | `originalFileName`, `mimeType` | Tên file và icon phân loại |

### 🟢 Thấp — Tính năng nâng cao

| # | API Endpoint | Trường cần thêm | Lý do |
|---|---|---|---|
| 10 | `GET /chat/rooms/{id}/messages` | `isEdited`, `replyToMessageId` | Chức năng thread/edit |
| 11 | `GET /chat/rooms` | `participantsCount`, `otherParticipantStatus` | Số thành viên & online status |
| 12 | `GET /tasks/{id}` | `storyPoints`, `tags` | Story points không hiển thị |

---

## 10. Đề xuất cấu trúc response chuẩn cho BE

Thay vì trả raw `userId/assigneeId/reporterId`, bọc trong object enriched:

```json
// Ví dụ TaskData enriched
{
  "taskId": 241,
  "title": "Tối ưu hóa connection pool",
  "assignee": {
    "userId": 5,
    "fullName": "Minh Quân",
    "avatarCode": "MQ",
    "role": "Tech Lead"
  },
  "reporter": {
    "userId": 3,
    "fullName": "Thu Hà",
    "avatarCode": "TH"
  },
  "epic": {
    "epicId": 4,
    "title": "Nâng cấp bảo mật & OAuth2",
    "color": "#6366f1"
  }
}

// Ví dụ NotificationData với actor riêng
{
  "notificationId": 12,
  "userId": 7,        // người nhận
  "actor": {
    "actorId": 5,     // người gây ra
    "actorName": "Minh Quân",
    "actorAvatarCode": "MQ",
    "actorRole": "Manager"
  },
  "title": "Minh Quân đã gán task cho bạn",
  "isUrgent": false
}
```
