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
[Route("api/v1/projects/{projectId:long}/members")]
public class ProjectMembersController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });

    [HttpGet]
    public async Task<IActionResult> List(long projectId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery, StringLength(200)] string? keyword = null,
        [FromQuery, Range(1, long.MaxValue)] long? departmentId = null,
        CancellationToken ct = default)
    {
        if (projectId <= 0) return Reply(400, "Mã dự án không hợp lệ.");
        if (!await db.Projects.AnyAsync(p => p.ProjectId == projectId && p.Status != "DELETED", ct))
            return Reply(404, "Không tìm thấy dự án.");
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == UserId, ct))
            return Reply(403, "Bạn không thuộc dự án này.");
        if (departmentId.HasValue && !await db.Departments.AnyAsync(d =>
            d.ProjectId == projectId && d.DepartmentId == departmentId, ct))
            return Reply(404, "Không tìm thấy phòng ban trong dự án.");
        var query = db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(m => m.User.FullName.Contains(keyword.Trim()));
        if (departmentId.HasValue) query = query.Where(m => m.DepartmentId == departmentId);
        var total = await query.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderBy(m => m.User.FullName).ThenBy(m => m.UserId)
            .Skip((int)skip).Take(pageSize).Select(m => new
            {
                m.UserId, m.User.FullName, m.User.Email, m.User.Avatar, m.User.Status,
                projectRole = m.Role.RoleName, m.DepartmentId,
                departmentName = m.Department == null ? null : m.Department.DepartmentName,
                m.JoinedAt
            }).ToListAsync(ct);
        return Reply(200, "Lấy danh sách thành viên thành công.", new
        {
            items = rows.Select(m => new
            {
                m.UserId, m.FullName, m.Email, m.Avatar, m.Status, m.projectRole,
                m.DepartmentId, m.departmentName,
                joinedAt = DateTime.SpecifyKind(m.JoinedAt, DateTimeKind.Utc)
            }), total, page, pageSize
        });
    }

    [HttpPost]
    public Task<IActionResult> Add(long projectId, AddProjectMemberRequest request, CancellationToken ct) =>
        Change(projectId, request.UserId, "ADD", ct);

    [HttpDelete("{userId:long}")]
    public Task<IActionResult> Remove(long projectId, long userId, CancellationToken ct) =>
        Change(projectId, userId, "REMOVE", ct);

    [HttpPatch("{userId:long}/role")]
    public Task<IActionResult> Promote(long projectId, long userId,
        PromoteProjectMemberRequest request, CancellationToken ct) =>
        Change(projectId, userId, "PROMOTE", ct);

    private async Task<IActionResult> Change(long projectId, long targetUserId, string operation, CancellationToken ct)
    {
        if (projectId <= 0 || targetUserId <= 0) return Reply(400, "Mã dự án hoặc người dùng không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // All membership changes serialize with project close/delete and each other.
        var project = await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH (UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct);
        if (project is null || project.Status == "DELETED") return Reply(404, "Không tìm thấy dự án.");
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == UserId &&
            m.Role.RoleName == "MANAGER" && m.RoleScope == "PROJECT", ct))
            return Reply(403, "Chỉ Manager của dự án được quản lý thành viên.");
        if (project.Status != "ACTIVE") return Reply(409, "Không thể thay đổi thành viên của dự án đã đóng.");

        var member = await db.ProjectMembers.Include(m => m.Role)
            .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == targetUserId, ct);
        if (operation == "ADD")
        {
            if (member is not null) return Reply(409, "Người dùng đã thuộc dự án.");
            var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == targetUserId, ct);
            if (target is null) return Reply(404, "Không tìm thấy người dùng.");
            if (target.Status != 1) return Reply(409, "Không thể thêm tài khoản đang bị khóa.");
            if (!await db.UserRoles.AnyAsync(r => r.UserId == targetUserId &&
                r.RoleScope == "SYSTEM" && r.Role.RoleName == "USER", ct))
                return Reply(400, "Chỉ thêm tài khoản có vai trò hệ thống USER.");
            var role = await db.Roles.SingleOrDefaultAsync(r => r.RoleName == "MEMBER" && r.Scope == "PROJECT", ct);
            if (role is null) return Reply(503, "Chưa cấu hình vai trò MEMBER.");
            member = new ProjectMember
            {
                ProjectId = projectId, UserId = targetUserId, RoleId = role.RoleId,
                RoleScope = "PROJECT", JoinedAt = DateTime.UtcNow
            };
            db.ProjectMembers.Add(member);
        }
        else
        {
            if (member is null) return Reply(404, "Không tìm thấy thành viên trong dự án.");
            if (operation == "PROMOTE")
            {
                if (member.Role.RoleName == "MANAGER") return Reply(409, "Thành viên đã là Manager.");
                if (!await db.Users.AnyAsync(u => u.UserId == targetUserId && u.Status == 1, ct))
                    return Reply(409, "Không thể nâng vai trò tài khoản đang bị khóa.");
                var role = await db.Roles.SingleOrDefaultAsync(r => r.RoleName == "MANAGER" && r.Scope == "PROJECT", ct);
                if (role is null) return Reply(503, "Chưa cấu hình vai trò MANAGER.");
                member.RoleId = role.RoleId;
            }
            else
            {
                if (member.Role.RoleName == "MANAGER" &&
                    !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId &&
                        m.UserId != targetUserId && m.Role.RoleName == "MANAGER", ct))
                    return Reply(409, "Dự án phải giữ ít nhất một Manager.");
                // Keep task history; unfinished assignments must be reassigned before removal.
                if (await db.Tasks.AnyAsync(t => t.ProjectId == projectId &&
                    t.AssigneeId == targetUserId && t.Status != "DONE", ct))
                    return Reply(409, "Cần chuyển giao các Task chưa hoàn thành trước khi loại bỏ thành viên.");
                db.ProjectMembers.Remove(member);
            }
        }
        var action = operation == "ADD" ? "MEMBER_ADDED" : operation == "REMOVE" ? "MEMBER_REMOVED" : "MEMBER_PROMOTED";
        db.ActivityLogs.Add(new ActivityLog
        {
            ProjectId = projectId, UserId = UserId, EntityType = "USER", EntityId = targetUserId,
            Action = action, Description = operation == "ADD" ? "Thêm thành viên dự án." :
                operation == "REMOVE" ? "Loại bỏ thành viên dự án." : "Nâng thành viên thành Manager.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(operation == "ADD" ? 201 : 200, operation == "ADD" ? "Thêm thành viên thành công." :
            operation == "REMOVE" ? "Loại bỏ thành viên thành công." : "Nâng vai trò thành công.",
            new { projectId, userId = targetUserId, projectRole = operation == "REMOVE" ? null :
                operation == "PROMOTE" ? "MANAGER" : "MEMBER" });
    }
}
