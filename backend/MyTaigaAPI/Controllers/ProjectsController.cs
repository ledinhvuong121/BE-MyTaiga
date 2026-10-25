using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public class ProjectsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });

    private static object Details(Project p, string? role) => new
    {
        p.ProjectId, p.ProjectName, p.Description, p.StartDate, p.EndDate,
        p.Status, p.CreatedBy, projectRole = role,
        createdAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
        updatedAt = p.UpdatedAt.HasValue
            ? DateTime.SpecifyKind(p.UpdatedAt.Value, DateTimeKind.Utc) : (DateTime?)null
    };

    [HttpPost]
    [Authorize(Roles = "USER")]
    public async Task<IActionResult> Create(ProjectRequest request, CancellationToken ct)
    {
        // Recheck the system role in the database, not a role supplied in the body.
        if (!await db.UserRoles.AnyAsync(x => x.UserId == UserId &&
            x.RoleScope == "SYSTEM" && x.Role.RoleName == "USER", ct))
            return Reply(403, "Chỉ tài khoản USER được tạo dự án.");
        if (!await db.Roles.AnyAsync(x => x.Scope == "PROJECT" && x.RoleName == "MANAGER", ct))
            return Reply(503, "Chưa cấu hình vai trò MANAGER.");

        var project = new Project
        {
            ProjectName = request.ProjectName.Trim(), Description = request.Description?.Trim(),
            StartDate = request.StartDate, EndDate = request.EndDate,
            Status = "ACTIVE", CreatedBy = UserId, CreatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        try
        {
            // The SQL trigger creates the manager membership, four columns and creation log atomically.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            return Reply(409, "Bạn đã có dự án mang tên này.");
        }
        Response.Headers.Location = $"/api/v1/projects/{project.ProjectId}";
        return Reply(201, "Tạo dự án thành công.", Details(project, "MANAGER"));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, StringLength(200)] string? keyword = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (status is not null && status != "ACTIVE" && status != "CLOSED")
            return Reply(400, "Trạng thái lọc chỉ nhận ACTIVE hoặc CLOSED.");
        var query = db.Projects.AsNoTracking().Where(p => p.Status != "DELETED" &&
            (p.CreatedBy == UserId || p.ProjectMembers.Any(m => m.UserId == UserId)));
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(p => p.ProjectName.Contains(keyword.Trim()));
        if (status is not null) query = query.Where(p => p.Status == status);
        var total = await query.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.ProjectId)
            .Skip((int)skip).Take(pageSize)
            .Select(p => new { project = p, role = p.ProjectMembers
                .Where(m => m.UserId == UserId).Select(m => m.Role.RoleName).FirstOrDefault() })
            .ToListAsync(ct);
        return Reply(200, "Lấy danh sách dự án thành công.", new
        {
            items = rows.Select(x => Details(x.project, x.role)), total, page, pageSize
        });
    }

    [HttpGet("{projectId:long}")]
    public async Task<IActionResult> Get(long projectId, CancellationToken ct)
    {
        if (projectId <= 0) return Reply(400, "Mã dự án không hợp lệ.");
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Status != "DELETED", ct);
        if (project is null) return Reply(404, "Không tìm thấy dự án.");
        var role = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == UserId)
            .Select(m => m.Role.RoleName).SingleOrDefaultAsync(ct);
        if (role is null) return Reply(403, "Bạn không thuộc dự án này.");
        return Reply(200, "Lấy chi tiết dự án thành công.", Details(project, role));
    }

    [HttpPut("{projectId:long}")]
    public Task<IActionResult> Update(long projectId, ProjectRequest request, CancellationToken ct) =>
        Change(projectId, "UPDATE", request, ct);

    [HttpPatch("{projectId:long}/close")]
    public Task<IActionResult> Close(long projectId, CancellationToken ct) =>
        Change(projectId, "CLOSE", null, ct);

    [HttpDelete("{projectId:long}")]
    public Task<IActionResult> Delete(long projectId, CancellationToken ct) =>
        Change(projectId, "DELETE", null, ct);

    private async Task<IActionResult> Change(long projectId, string operation,
        ProjectRequest? request, CancellationToken ct)
    {
        if (projectId <= 0) return Reply(400, "Mã dự án không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize status changes so a concurrent update cannot overwrite a close/delete.
        var project = await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH (UPDLOCK, HOLDLOCK) WHERE ProjectId = {projectId}
            """).SingleOrDefaultAsync(ct);
        if (project is null || project.Status == "DELETED")
            return Reply(404, "Không tìm thấy dự án.");
        var manager = await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId &&
            m.UserId == UserId && m.RoleScope == "PROJECT" && m.Role.RoleName == "MANAGER", ct);
        if (!manager) return Reply(403, "Chỉ Manager của dự án được thực hiện thao tác này.");
        if (operation != "DELETE" && project.Status != "ACTIVE")
            return Reply(409, "Dự án đã đóng, không thể cập nhật hoặc đóng lại.");
        if (operation == "UPDATE")
        {
            project.ProjectName = request!.ProjectName.Trim();
            project.Description = request.Description?.Trim();
            project.StartDate = request.StartDate;
            project.EndDate = request.EndDate;
        }
        else project.Status = operation == "CLOSE" ? "CLOSED" : "DELETED";
        project.UpdatedAt = DateTime.UtcNow;
        db.ActivityLogs.Add(new ActivityLog
        {
            ProjectId = projectId, UserId = UserId, EntityId = projectId, EntityType = "PROJECT",
            Action = operation == "UPDATE" ? "PROJECT_UPDATED" : operation == "CLOSE" ? "PROJECT_CLOSED" : "PROJECT_DELETED",
            Description = operation == "UPDATE" ? "Cập nhật thông tin dự án." :
                operation == "CLOSE" ? "Đóng dự án." : "Xóa mềm dự án.",
            CreatedAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            return Reply(409, "Người tạo đã có dự án mang tên này.");
        }
        return Reply(200, operation == "UPDATE" ? "Cập nhật dự án thành công." :
            operation == "CLOSE" ? "Đóng dự án thành công." : "Xóa dự án thành công.", Details(project, "MANAGER"));
    }

    private static bool IsDuplicate(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
