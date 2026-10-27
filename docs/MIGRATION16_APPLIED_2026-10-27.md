# Áp dụng migration 16 vào ScrumbanDB

Ngày: 27/10/2026. Thực hiện sau khi chủ dự án yêu cầu chạy script.
SQL Server: .\SQLEXPRESS. Database: ScrumbanDB.
Script: Database/16_FrontendMissingData.sql. sqlcmd dùng -E -C -I -b.

Kết quả: thành công. Đủ 9 cột nullable đúng kiểu, TagsJson NVARCHAR(MAX).
Hai CHECK constraint CK_Task_StoryPoints và CK_Task_TagsJson đều bật và trusted
(is_disabled=0, is_not_trusted=0).

| Bảng | Số bản ghi trước | Số bản ghi sau |
|---|---:|---:|
| Task | 0 | 0 |
| Epic | 0 | 0 |
| Notification | 0 | 0 |
| ActivityLog | 43 | 43 |
| Message | 0 | 0 |

Migration chỉ thay schema. Không upload ảnh, không sửa mật khẩu/secret, không chuyển
ảnh cũ lên Cloudinary. Cấu hình Cloudinary và kiểm thử upload thật vẫn còn cần thực hiện.
