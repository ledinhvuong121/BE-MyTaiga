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
[Route("api/v1/projects/{projectId:long}/departments")]
public class ProjectDepartmentsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });

    [HttpGet]
    public async Task<IActionResult> List(long projectId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        if (projectId <= 0) return Reply(400, "Mã dự án không hợp lệ.");
        if (!await db.Projects.AnyAsync(p => p.ProjectId == projectId && p.Status != "DELETED", ct))
            return Reply(404, "Không tìm thấy dự án.");
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == UserId, ct))
            return Reply(403, "Bạn không thuộc dự án này.");
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var query = db.Departments.AsNoTracking().Where(d => d.ProjectId == projectId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.DepartmentName).ThenBy(d => d.DepartmentId)
            .Skip((int)skip).Take(pageSize).Select(d => new
            {
                d.DepartmentId, d.ProjectId, d.DepartmentName, d.Description,
                memberCount = d.ProjectMembers.Count()
            }).ToListAsync(ct);
        return Reply(200, "Lấy danh sách phòng ban thành công.", new { items, total, page, pageSize });
    }

    [HttpPost]
    public Task<IActionResult> Create(long projectId, DepartmentRequest request, CancellationToken ct) =>
        Change(projectId, null, "CREATE", request, null, ct);

    [HttpPut("{departmentId:long}")]
    public Task<IActionResult> Update(long projectId, long departmentId, DepartmentRequest request, CancellationToken ct) =>
        Change(projectId, departmentId, "UPDATE", request, null, ct);

    [HttpDelete("{departmentId:long}")]
    public Task<IActionResult> Delete(long projectId, long departmentId, CancellationToken ct) =>
        Change(projectId, departmentId, "DELETE", null, null, ct);

    [HttpPatch("/api/v1/projects/{projectId:long}/members/{userId:long}/department")]
    public Task<IActionResult> Assign(long projectId, long userId, AssignDepartmentRequest request, CancellationToken ct) =>
        Change(projectId, request.DepartmentId, "ASSIGN", null, userId, ct);

    private async Task<IActionResult> Change(long projectId, long? departmentId, string operation,
        DepartmentRequest? request, long? targetUserId, CancellationToken ct)
    {
        if (projectId <= 0 || departmentId is <= 0 || targetUserId is <= 0)
            return Reply(400, "Mã dự án, phòng ban hoặc người dùng không hợp lệ.");
        if (request is not null && string.IsNullOrWhiteSpace(request.DepartmentName))
            return Reply(400, "Tên phòng ban không được để trống.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH (UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct);
        if (project is null || project.Status == "DELETED") return Reply(404, "Không tìm thấy dự án.");
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == UserId &&
            m.Role.RoleName == "MANAGER" && m.RoleScope == "PROJECT", ct))
            return Reply(403, "Chỉ Manager của dự án được quản lý phòng ban.");
        if (project.Status != "ACTIVE") return Reply(409, "Không thể thay đổi phòng ban của dự án đã đóng.");
        Department? department = null;
        if (departmentId.HasValue)
        {
            department = await db.Departments.SingleOrDefaultAsync(d =>
                d.ProjectId == projectId && d.DepartmentId == departmentId, ct);
            if (department is null) return Reply(404, "Không tìm thấy phòng ban trong dự án.");
        }
        if (operation == "CREATE")
        {
            department = new Department
            {
                ProjectId = projectId, DepartmentName = request!.DepartmentName.Trim(),
                Description = request.Description?.Trim()
            };
            db.Departments.Add(department);
        }
        else if (operation == "UPDATE")
        {
            department!.DepartmentName = request!.DepartmentName.Trim();
            department.Description = request.Description?.Trim();
        }
        else if (operation == "DELETE")
        {
            if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.DepartmentId == departmentId, ct))
                return Reply(409, "Cần chuyển hoặc bỏ gán thành viên trước khi xóa phòng ban.");
            db.Departments.Remove(department!);
        }
        else
        {
            var member = await db.ProjectMembers.SingleOrDefaultAsync(m =>
                m.ProjectId == projectId && m.UserId == targetUserId, ct);
            if (member is null) return Reply(404, "Không tìm thấy thành viên trong dự án.");
            if (member.DepartmentId == departmentId) return Reply(409, "Thành viên đã có phân công phòng ban này.");
            member.DepartmentId = departmentId;
        }
        try
        {
            // Obtain identity before writing its audit entry, still in the same transaction.
            await db.SaveChangesAsync(ct);
            db.ActivityLogs.Add(new ActivityLog
            {
                ProjectId = projectId, UserId = UserId,
                EntityType = operation == "ASSIGN" ? "USER" : "DEPARTMENT",
                EntityId = operation == "ASSIGN" ? targetUserId : department!.DepartmentId,
                Action = operation == "ASSIGN" ? "MEMBER_DEPARTMENT_CHANGED" : "DEPARTMENT_" +
                    (operation == "CREATE" ? "CREATED" : operation == "UPDATE" ? "UPDATED" : "DELETED"),
                Description = operation == "ASSIGN" ? $"Gán phòng ban: {departmentId?.ToString() ?? "không có"}." :
                    $"{operation}: {department!.DepartmentName}", CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql &&
            (sql.Number == 2601 || sql.Number == 2627))
        {
            return Reply(409, "Tên phòng ban đã tồn tại trong dự án.");
        }
        object data = operation == "ASSIGN" ? new { projectId, userId = targetUserId, departmentId } :
            new { projectId, departmentId = department!.DepartmentId, department.DepartmentName, department.Description };
        return Reply(operation == "CREATE" ? 201 : 200, "Thao tác phòng ban thành công.", data);
    }
}
