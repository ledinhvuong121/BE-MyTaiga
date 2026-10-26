# Tệp Task — UC27

POST `/api/v1/tasks/{id}/attachments`, multipart field **files** (1–10 tệp).
GET cùng URL có page/pageSize để liệt kê. GET `/api/v1/attachments/{id}` metadata,
GET `.../{id}/preview`, GET `.../{id}/download`, DELETE `.../{id}`.
Bearer JWT và membership đúng dự án; uploader hoặc Manager được xóa.
Project CLOSED chỉ đọc; Sprint CLOSED vẫn đính kèm chứng cứ được nếu project ACTIVE.

Mỗi tệp <=min(MAX_FILE_SIZE_MB,25) MiB; extension lấy giao ALLOWED_FILE_EXTENSIONS với
PDF/TXT/PNG/JPG/JPEG/ZIP/DOCX/XLSX/PPTX. Không cho config bật executable.
Kiểm tra magic/codec/ZIP container, text UTF-8 nghiêm ngặt, ảnh <=8192 chiều và <=16 triệu pixel;
ZIP<=10000 mục/256MiB kích thước khai báo; Office phải đúng main part, không vbaProject.bin.
Đây là kiểm tra loại/cấu trúc, không phải công cụ antivirus hay chứng nhận nội dung hoàn toàn an toàn.

Lưu ngoài wwwroot tại App_Data/task-files (hoặc Storage:TaskFilesRoot), tên GUID;
DB chỉ lưu storage key, không trả FilePath. Tên client được lấy basename, <=200, không control/invalid chars.
Upload batch atomic SQL + cleanup các tệp đã tạo khi validation/cancel/save lỗi.
Delete commit metadata trước rồi cleanup file; lỗi disk cleanup được log, orphan không còn URL truy cập.
Không chạy recursive delete hoặc xóa thư mục người dùng. Tệp thật không đi kèm Git.

Preview chỉ image/PDF/TXT; Office/ZIP trả 415, có download. Response nosniff/no-store/CSP sandbox;
download có attachment disposition, hỗ trợ Range sau auth. File thiếu trên disk trả 404.
Frontend dùng fetch có Bearer rồi blob để preview/download, không trông chờ img URL tự mang JWT.

POST 201 data={items}; list 200 data={items,total,page,pageSize}; metadata item gồm
attachmentId,taskId,fileName,fileSize,uploadedBy,uploadedAt,previewUrl,downloadUrl.
401 auth;403 scope/delete permission;404 ID/disk/scope;409 project closed;400 invalid files;415 preview unsupported.

Kiểm thử: `python tests/attachments_integration.py`, backend chạy qua `tests/start-isolated.ps1`
với storage test riêng. 2026-10-26: 33 kiểm tra multipart HTTP+SQL+filesystem đạt,
64 kiểm tra Bug hồi quy đạt. Có encoding invalid đã bắt/sửa, fake extensions,
macro/ZIP metadata, giới hạn config/count/size, batch rollback không orphan, private paths,
preview/download/range, quyền xóa và CLOSED/DELETED. Các file test không nằm trong storage thật.
