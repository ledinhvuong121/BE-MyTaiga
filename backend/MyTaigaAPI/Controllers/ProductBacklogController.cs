using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/product-backlog-items")]
public class ProductBacklogController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });
    private static object Details(ProductBacklogItem p) => new
    {
        pbiId = p.Pbiid, p.EpicId, p.Title, p.Description, p.StoryPoint, p.Priority, p.Status,
        createdAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
        updatedAt = p.UpdatedAt.HasValue ? DateTime.SpecifyKind(p.UpdatedAt.Value, DateTimeKind.Utc) : (DateTime?)null
    };
    private async Task<int> Access(long projectId, bool write, CancellationToken ct)
    {
        var project = write ? await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH (UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct) : await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (project is null || project.Status == "DELETED") return 404;
        var role = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == UserId)
            .Select(m => m.Role.RoleName).SingleOrDefaultAsync(ct);
        if (role is null || (write && role != "MANAGER")) return 403;
        return write && project.Status != "ACTIVE" ? 409 : 200;
    }
    private ObjectResult Denied(int code) => Reply(code, code == 404 ? "Không tìm thấy dự án." :
        code == 403 ? "Bạn không có quyền thực hiện trong dự án này." : "Dự án đã đóng.");

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, long.MaxValue)] long projectId,
        [FromQuery, Range(1, long.MaxValue)] long? epicId = null,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, StringLength(255)] string? keyword = null,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        if (projectId <= 0) return Reply(400, "Cần cung cấp projectId hợp lệ.");
        var access = await Access(projectId, false, ct);
        if (access != 200) return Denied(access);
        if (status is not null && !new[] { "NEW", "READY", "IN_SPRINT", "DONE" }.Contains(status))
            return Reply(400, "Trạng thái PBI không hợp lệ.");
        if (epicId.HasValue && !await db.Epics.AnyAsync(e => e.EpicId == epicId && e.ProjectId == projectId, ct))
            return Reply(404, "Không tìm thấy Epic trong dự án.");
        var query = db.ProductBacklogItems.AsNoTracking().Where(p => p.Epic.ProjectId == projectId);
        if (epicId.HasValue) query = query.Where(p => p.EpicId == epicId);
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(p => p.Title.Contains(keyword.Trim()));
        var total = await query.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderBy(p => p.Priority).ThenBy(p => p.Pbiid).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200, "Lấy Product Backlog thành công.", new { items = rows.Select(Details), total, page, pageSize });
    }

    [HttpGet("{pbiId:long}")]
    public async Task<IActionResult> Get(long pbiId, CancellationToken ct)
    {
        if (pbiId <= 0) return Reply(400, "Mã PBI không hợp lệ.");
        var item = await db.ProductBacklogItems.AsNoTracking().Include(p => p.Epic).SingleOrDefaultAsync(p => p.Pbiid == pbiId, ct);
        if (item is null) return Reply(404, "Không tìm thấy PBI.");
        var access = await Access(item.Epic.ProjectId, false, ct);
        return access == 200 ? Reply(200, "Lấy chi tiết PBI thành công.", Details(item)) : Denied(access);
    }

    [HttpPost]
    public Task<IActionResult> Create(BacklogRequest request, CancellationToken ct) => Change(null, request, false, null, ct);

    [HttpPut("{pbiId:long}")]
    public Task<IActionResult> Update(long pbiId, BacklogRequest request, CancellationToken ct) => Change(pbiId, request, false, null, ct);

    [HttpDelete("{pbiId:long}")]
    public Task<IActionResult> Delete(long pbiId, CancellationToken ct) => Change(pbiId, null, true, null, ct);

    [HttpPatch("{pbiId:long}/priority")]
    public Task<IActionResult> Priority(long pbiId, BacklogPriorityRequest request, CancellationToken ct) => Change(pbiId, null, false, request.Priority, ct);

    private async Task<IActionResult> Change(long? id, BacklogRequest? request, bool delete, int? priority, CancellationToken ct)
    {
        if (id is <= 0) return Reply(400, "Mã PBI không hợp lệ.");
        if (request is not null && string.IsNullOrWhiteSpace(request.Title)) return Reply(400, "Tiêu đề không được trống.");
        var projectId = id.HasValue
            ? await db.ProductBacklogItems.Where(p => p.Pbiid == id).Select(p => (long?)p.Epic.ProjectId).SingleOrDefaultAsync(ct)
            : await db.Epics.Where(e => e.EpicId == request!.EpicId).Select(e => (long?)e.ProjectId).SingleOrDefaultAsync(ct);
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy PBI hoặc Epic.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return Denied(access);
        if (request is not null && !await db.Epics.AnyAsync(e => e.EpicId == request.EpicId && e.ProjectId == projectId, ct))
            return Reply(404, "Epic không thuộc dự án này.");
        ProductBacklogItem item;
        if (id.HasValue)
        {
            var existing = await db.ProductBacklogItems.Include(p => p.Epic).SingleOrDefaultAsync(p => p.Pbiid == id, ct);
            if (existing is null || existing.Epic.ProjectId != projectId) return Reply(404, "Không tìm thấy PBI trong dự án.");
            item = existing;
        }
        else
        {
            item = new ProductBacklogItem { CreatedAt = DateTime.UtcNow };
            db.ProductBacklogItems.Add(item);
        }
        var linked = id.HasValue && await db.SprintBacklogItems.AnyAsync(b => b.Pbiid == id && b.Sprint.Status != "CLOSED", ct);
        if (delete)
        {
            if (id.HasValue && await db.SprintBacklogItems.AnyAsync(b => b.Pbiid == id, ct))
                return Reply(409, "Không thể xóa PBI đã liên kết Sprint.");
            db.ProductBacklogItems.Remove(item);
        }
        else if (priority.HasValue) item.Priority = priority.Value;
        else
        {
            if (request!.Status == "IN_SPRINT" && !linked)
                return Reply(409, "Cần đưa PBI vào Sprint trước khi chuyển IN_SPRINT.");
            if (linked && (request.Status == "NEW" || request.Status == "READY"))
                return Reply(409, "Cần loại PBI khỏi Sprint trước khi chuyển về Backlog.");
            item.EpicId = request.EpicId; item.Title = request.Title.Trim(); item.Description = request.Description?.Trim();
            item.StoryPoint = request.StoryPoint; item.Priority = request.Priority; item.Status = request.Status;
        }
        if (!delete && id.HasValue) item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Log(projectId.Value, item.Pbiid, delete ? "PBI_DELETED" : priority.HasValue ? "PBI_PRIORITY_CHANGED" : id.HasValue ? "PBI_UPDATED" : "PBI_CREATED");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(id.HasValue ? 200 : 201, "Thao tác Product Backlog thành công.", Details(item));
    }

    [HttpPut("order")]
    public async Task<IActionResult> Order(BacklogOrderRequest request, CancellationToken ct)
    {
        if (request.PbiIds.Any(id => id <= 0) || request.PbiIds.Distinct().Count() != request.PbiIds.Count)
            return Reply(400, "Danh sách PBI có mã sai hoặc trùng lặp.");
        var projectId = await db.Epics.Where(e => e.EpicId == request.EpicId).Select(e => (long?)e.ProjectId).SingleOrDefaultAsync(ct);
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy Epic.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return Denied(access);
        if (!await db.Epics.AnyAsync(e => e.EpicId == request.EpicId && e.ProjectId == projectId, ct))
            return Reply(404, "Không tìm thấy Epic.");
        var items = await db.ProductBacklogItems.Where(p => p.EpicId == request.EpicId).ToDictionaryAsync(p => p.Pbiid, ct);
        if (items.Count != request.PbiIds.Count || request.PbiIds.Any(id => !items.ContainsKey(id)))
            return Reply(409, "Phải gửi đủ và đúng các PBI hiện có của Epic. Hãy tải lại danh sách.");
        var now = DateTime.UtcNow;
        for (var i = 0; i < request.PbiIds.Count; i++)
        {
            var item = items[request.PbiIds[i]]; item.Priority = i + 1; item.UpdatedAt = now;
        }
        db.ActivityLogs.Add(new ActivityLog
        {
            ProjectId = projectId.Value, UserId = UserId, EntityType = "EPIC", EntityId = request.EpicId,
            Action = "PBI_REORDERED", Description = "Sắp xếp lại PBI của Epic.", CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(200, "Sắp xếp Product Backlog thành công.", new { request.EpicId, request.PbiIds });
    }

    private void Log(long projectId, long id, string action) => db.ActivityLogs.Add(new ActivityLog
    {
        ProjectId = projectId, UserId = UserId, EntityType = "PBI", EntityId = id,
        Action = action, Description = action, CreatedAt = DateTime.UtcNow
    });
}
