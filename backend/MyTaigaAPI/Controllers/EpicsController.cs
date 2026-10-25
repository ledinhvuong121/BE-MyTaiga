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
public class EpicsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });
    private static object Details(Epic e) => new
    {
        e.EpicId, e.ProjectId, e.Title, e.Description, e.Priority, e.Status, e.CreatedBy,
        createdAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
        updatedAt = e.UpdatedAt.HasValue ? DateTime.SpecifyKind(e.UpdatedAt.Value, DateTimeKind.Utc) : (DateTime?)null
    };
    private async Task<int> Access(long projectId, bool write, CancellationToken ct)
    {
        if (projectId <= 0) return 400;
        var project = write
            ? await db.Projects.FromSqlInterpolated($"""
                SELECT * FROM dbo.Project WITH (UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
                """).SingleOrDefaultAsync(ct)
            : await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (project is null || project.Status == "DELETED") return 404;
        var role = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == UserId)
            .Select(m => m.Role.RoleName).SingleOrDefaultAsync(ct);
        if (role is null || (write && role != "MANAGER")) return 403;
        return write && project.Status != "ACTIVE" ? 409 : 200;
    }
    private ObjectResult AccessError(int code) => Reply(code, code switch
    {
        400 => "Mã dự án không hợp lệ.", 404 => "Không tìm thấy dự án.",
        403 => "Bạn không có quyền thực hiện trong dự án này.", _ => "Dự án đã đóng."
    });

    [HttpGet("api/v1/projects/{projectId:long}/epics")]
    public async Task<IActionResult> List(long projectId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, StringLength(255)] string? keyword = null,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var access = await Access(projectId, false, ct);
        if (access != 200) return AccessError(access);
        if (status is not null && !new[] { "OPEN", "IN_PROGRESS", "DONE", "CLOSED" }.Contains(status))
            return Reply(400, "Trạng thái Epic không hợp lệ.");
        var query = db.Epics.AsNoTracking().Where(e => e.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(e => e.Title.Contains(keyword.Trim()));
        if (status is not null) query = query.Where(e => e.Status == status);
        var total = await query.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderBy(e => e.Priority).ThenBy(e => e.EpicId)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200, "Lấy danh sách Epic thành công.", new
        { items = rows.Select(Details), total, page, pageSize });
    }

    [HttpGet("api/v1/epics/{epicId:long}")]
    public async Task<IActionResult> Get(long epicId, CancellationToken ct)
    {
        if (epicId <= 0) return Reply(400, "Mã Epic không hợp lệ.");
        var epic = await db.Epics.AsNoTracking().SingleOrDefaultAsync(e => e.EpicId == epicId, ct);
        if (epic is null) return Reply(404, "Không tìm thấy Epic.");
        var access = await Access(epic.ProjectId, false, ct);
        return access == 200 ? Reply(200, "Lấy chi tiết Epic thành công.", Details(epic)) : AccessError(access);
    }

    [HttpGet("api/v1/epics/{epicId:long}/progress")]
    public async Task<IActionResult> Progress(long epicId, CancellationToken ct)
    {
        if (epicId <= 0) return Reply(400, "Mã Epic không hợp lệ.");
        var epic = await db.Epics.AsNoTracking().SingleOrDefaultAsync(e => e.EpicId == epicId, ct);
        if (epic is null) return Reply(404, "Không tìm thấy Epic.");
        var access = await Access(epic.ProjectId, false, ct);
        if (access != 200) return AccessError(access);
        var pbis = await db.ProductBacklogItems.Where(p => p.EpicId == epicId).Select(p => p.Status).ToListAsync(ct);
        var tasks = await db.Tasks.Where(t => t.EpicId == epicId).Select(t => t.Status).ToListAsync(ct);
        var completedPbis = pbis.Count(s => s == "DONE");
        var completedTasks = tasks.Count(s => s == "DONE");
        return Reply(200, "Lấy tiến độ Epic thành công.", new
        {
            epicId, totalPbis = pbis.Count, completedPbis,
            pbiProgressPercent = pbis.Count == 0 ? 0 : Math.Round(100m * completedPbis / pbis.Count, 2),
            totalTasks = tasks.Count, completedTasks,
            taskProgressPercent = tasks.Count == 0 ? 0 : Math.Round(100m * completedTasks / tasks.Count, 2)
        });
    }

    [HttpPost("api/v1/projects/{projectId:long}/epics")]
    public Task<IActionResult> Create(long projectId, EpicRequest request, CancellationToken ct) =>
        Change(projectId, null, request, false, ct);

    [HttpPut("api/v1/epics/{epicId:long}")]
    public Task<IActionResult> Update(long epicId, EpicRequest request, CancellationToken ct) =>
        Change(null, epicId, request, false, ct);

    [HttpDelete("api/v1/epics/{epicId:long}")]
    public Task<IActionResult> Delete(long epicId, CancellationToken ct) =>
        Change(null, epicId, null, true, ct);

    private async Task<IActionResult> Change(long? projectId, long? epicId, EpicRequest? request, bool delete, CancellationToken ct)
    {
        if (epicId is <= 0 || projectId is <= 0) return Reply(400, "Mã không hợp lệ.");
        if (request is not null && string.IsNullOrWhiteSpace(request.Title)) return Reply(400, "Tiêu đề không được trống.");
        if (epicId.HasValue)
        {
            projectId = await db.Epics.Where(e => e.EpicId == epicId).Select(e => (long?)e.ProjectId).SingleOrDefaultAsync(ct);
            if (!projectId.HasValue) return Reply(404, "Không tìm thấy Epic.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId!.Value, true, ct);
        if (access != 200) return AccessError(access);
        Epic epic;
        if (!epicId.HasValue)
        {
            epic = new Epic { ProjectId = projectId.Value, CreatedBy = UserId, CreatedAt = DateTime.UtcNow };
            db.Epics.Add(epic);
        }
        else
        {
            var existing = await db.Epics.SingleOrDefaultAsync(e => e.EpicId == epicId && e.ProjectId == projectId, ct);
            if (existing is null) return Reply(404, "Không tìm thấy Epic.");
            epic = existing;
        }
        if (delete)
        {
            if (await db.ProductBacklogItems.AnyAsync(p => p.EpicId == epicId, ct) ||
                await db.Tasks.AnyAsync(t => t.EpicId == epicId, ct))
                return Reply(409, "Cần chuyển hoặc xóa các PBI/Task liên kết trước khi xóa Epic.");
            db.Epics.Remove(epic);
        }
        else
        {
            epic.Title = request!.Title.Trim(); epic.Description = request.Description?.Trim();
            epic.Priority = request.Priority; epic.Status = request.Status;
            if (epicId.HasValue) epic.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        Log(projectId.Value, epic.EpicId, "EPIC", delete ? "EPIC_DELETED" : epicId.HasValue ? "EPIC_UPDATED" : "EPIC_CREATED");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(epicId.HasValue ? 200 : 201, "Thao tác Epic thành công.", Details(epic));
    }

    [HttpPatch("api/v1/pbis/{pbiId:long}/epic")]
    [HttpPatch("api/v1/product-backlog-items/{pbiId:long}/epic")]
    public Task<IActionResult> LinkPbi(long pbiId, LinkEpicRequest request, CancellationToken ct) =>
        Link(pbiId, request.EpicId, false, ct);

    [HttpPatch("api/v1/tasks/{taskId:long}/epic")]
    public Task<IActionResult> LinkTask(long taskId, LinkEpicRequest request, CancellationToken ct) =>
        Link(taskId, request.EpicId, true, ct);

    private async Task<IActionResult> Link(long itemId, long epicId, bool task, CancellationToken ct)
    {
        if (itemId <= 0) return Reply(400, "Mã công việc không hợp lệ.");
        var projectId = task
            ? await db.Tasks.Where(t => t.TaskId == itemId).Select(t => (long?)t.ProjectId).SingleOrDefaultAsync(ct)
            : await db.ProductBacklogItems.Where(p => p.Pbiid == itemId).Select(p => (long?)p.Epic.ProjectId).SingleOrDefaultAsync(ct);
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy công việc.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return AccessError(access);
        if (!await db.Epics.AnyAsync(e => e.EpicId == epicId && e.ProjectId == projectId, ct))
            return Reply(404, "Epic không thuộc dự án này.");
        if (task)
        {
            var item = await db.Tasks.SingleOrDefaultAsync(t => t.TaskId == itemId && t.ProjectId == projectId, ct);
            if (item is null) return Reply(404, "Không tìm thấy Task.");
            if (item.EpicId == epicId) return Reply(409, "Task đã liên kết Epic này.");
            item.EpicId = epicId; item.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var item = await db.ProductBacklogItems.Include(p => p.Epic).SingleOrDefaultAsync(p => p.Pbiid == itemId, ct);
            if (item is null || item.Epic.ProjectId != projectId) return Reply(404, "Không tìm thấy PBI trong dự án.");
            if (item.EpicId == epicId) return Reply(409, "PBI đã liên kết Epic này.");
            item.EpicId = epicId; item.UpdatedAt = DateTime.UtcNow;
        }
        Log(projectId.Value, itemId, task ? "TASK" : "PBI", "EPIC_LINKED");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(200, "Liên kết Epic thành công.", new { itemId, epicId });
    }

    private void Log(long projectId, long entityId, string entityType, string action) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            ProjectId = projectId, UserId = UserId, EntityId = entityId, EntityType = entityType,
            Action = action, Description = action, CreatedAt = DateTime.UtcNow
        });
}
