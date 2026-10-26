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
[Route("api/v1/sprints")]
public class SprintsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });
    private static object Details(Sprint s) => new { s.SprintId, s.ProjectId, s.SprintName, s.SprintGoal, s.StartDate, s.EndDate, s.Status };
    private async Task<int> Access(long projectId, bool write, CancellationToken ct)
    {
        if (projectId <= 0) return 400;
        var project = write ? await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH (UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct) : await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (project is null || project.Status == "DELETED") return 404;
        var role = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == UserId)
            .Select(m => m.Role.RoleName).SingleOrDefaultAsync(ct);
        if (role is null || (write && role != "MANAGER")) return 403;
        return write && project.Status != "ACTIVE" ? 409 : 200;
    }
    private ObjectResult Denied(int code) => Reply(code, code switch
    { 400 => "Mã dự án không hợp lệ.", 404 => "Không tìm thấy dự án.", 403 => "Bạn không có quyền trong dự án này.", _ => "Dự án đã đóng." });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long projectId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var access = await Access(projectId, false, ct);
        if (access != 200) return Denied(access);
        if (status is not null && !new[] { "PLANNED", "ACTIVE", "CLOSED" }.Contains(status)) return Reply(400, "Trạng thái Sprint không hợp lệ.");
        var query = db.Sprints.AsNoTracking().Where(s => s.ProjectId == projectId);
        if (status is not null) query = query.Where(s => s.Status == status);
        var total = await query.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderBy(s => s.StartDate).ThenBy(s => s.SprintId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200, "Lấy danh sách Sprint thành công.", new { items = rows.Select(Details), total, page, pageSize });
    }

    [HttpGet("{sprintId:long}")]
    public async Task<IActionResult> Get(long sprintId, CancellationToken ct)
    {
        if (sprintId <= 0) return Reply(400, "Mã Sprint không hợp lệ.");
        var sprint = await db.Sprints.AsNoTracking().SingleOrDefaultAsync(s => s.SprintId == sprintId, ct);
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        var access = await Access(sprint.ProjectId, false, ct);
        return access == 200 ? Reply(200, "Lấy chi tiết Sprint thành công.", Details(sprint)) : Denied(access);
    }

    [HttpPost]
    public Task<IActionResult> Create(SprintRequest request, CancellationToken ct) => Save(null, request, "CREATE", ct);
    [HttpPut("{sprintId:long}")]
    public Task<IActionResult> Update(long sprintId, SprintRequest request, CancellationToken ct) => Save(sprintId, request, "UPDATE", ct);
    [HttpDelete("{sprintId:long}")]
    public Task<IActionResult> Delete(long sprintId, CancellationToken ct) => Save(sprintId, null, "DELETE", ct);
    [HttpPost("{sprintId:long}/start")]
    public Task<IActionResult> Start(long sprintId, CancellationToken ct) => Save(sprintId, null, "START", ct);
    [HttpPost("{sprintId:long}/close")]
    public Task<IActionResult> Close(long sprintId, CancellationToken ct) => Save(sprintId, null, "CLOSE", ct);

    private async Task<IActionResult> Save(long? id, SprintRequest? request, string operation, CancellationToken ct)
    {
        if (id is <= 0) return Reply(400, "Mã Sprint không hợp lệ.");
        var projectId = id.HasValue ? await db.Sprints.Where(s => s.SprintId == id).Select(s => (long?)s.ProjectId).SingleOrDefaultAsync(ct) : request!.ProjectId;
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy Sprint.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return Denied(access);
        var sprint = id.HasValue ? await db.Sprints.SingleOrDefaultAsync(s => s.SprintId == id, ct) :
            new Sprint { ProjectId = projectId.Value, Status = "PLANNED" };
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        if (id.HasValue && sprint.Status == "CLOSED") return Reply(409, "Sprint đã đóng, không thể thay đổi.");
        if (request is not null)
        {
            if (request.ProjectId != projectId) return Reply(400, "Không thể chuyển Sprint sang dự án khác.");
            if (await db.Sprints.AnyAsync(s => s.ProjectId == projectId && s.SprintId != id.GetValueOrDefault() &&
                s.SprintName == request.SprintName.Trim(), ct)) return Reply(409, "Tên Sprint đã tồn tại trong dự án.");
            if (await db.Sprints.AnyAsync(s => s.ProjectId == projectId && s.SprintId != id.GetValueOrDefault() &&
                s.Status != "CLOSED" && s.StartDate <= request.EndDate && request.StartDate <= s.EndDate, ct))
                return Reply(409, "Thời gian chồng lấn với Sprint chưa đóng.");
            if (sprint.Status == "ACTIVE" && (sprint.StartDate != request.StartDate || sprint.EndDate != request.EndDate))
                return Reply(409, "Không đổi lịch của Sprint đang chạy.");
            sprint.SprintName = request.SprintName.Trim(); sprint.SprintGoal = request.SprintGoal?.Trim();
            sprint.StartDate = request.StartDate; sprint.EndDate = request.EndDate;
        }
        if (!id.HasValue) db.Sprints.Add(sprint);
        if (operation == "DELETE")
        {
            if (sprint.Status != "PLANNED" || await db.Tasks.AnyAsync(t => t.SprintId == id, ct) ||
                await db.FlowMetrics.AnyAsync(f => f.SprintId == id, ct) ||
                await db.TaskCompletionHistories.AnyAsync(h => h.SprintId == id, ct))
                return Reply(409, "Chỉ xóa Sprint dự kiến chưa có Task hoặc lịch sử/số liệu.");
            var links = await db.SprintBacklogItems.Include(b => b.Pbi).Where(b => b.SprintId == id).ToListAsync(ct);
            foreach (var link in links) if (link.Pbi.Status == "IN_SPRINT") link.Pbi.Status = "READY";
            db.SprintBacklogItems.RemoveRange(links);
            await db.SaveChangesAsync(ct);
            db.Sprints.Remove(sprint);
        }
        if (operation == "START")
        {
            if (sprint.Status != "PLANNED") return Reply(409, "Chỉ bắt đầu Sprint PLANNED.");
            if (await db.Sprints.AnyAsync(s => s.ProjectId == projectId && s.Status == "ACTIVE", ct)) return Reply(409, "Dự án đã có Sprint đang chạy.");
            if (!await db.SprintBacklogItems.AnyAsync(b => b.SprintId == id, ct)) return Reply(409, "Cần chọn PBI trước khi bắt đầu Sprint.");
            sprint.Status = "ACTIVE";
        }
        if (operation == "CLOSE")
        {
            if (sprint.Status != "ACTIVE") return Reply(409, "Chỉ đóng Sprint đang chạy.");
            var links = await db.SprintBacklogItems.Include(b => b.Pbi).Where(b => b.SprintId == id).ToListAsync(ct);
            foreach (var link in links) { link.StoryPointAtClose = link.Pbi.StoryPoint; link.StatusAtClose = link.Pbi.Status; }
            sprint.Status = "CLOSED";
            await db.SaveChangesAsync(ct); // Freeze history before returning incomplete PBIs to READY.
            foreach (var link in links) if (link.Pbi.Status != "DONE") { link.Pbi.Status = "READY"; link.Pbi.UpdatedAt = DateTime.UtcNow; }
        }
        await db.SaveChangesAsync(ct);
        Log(projectId.Value, sprint.SprintId, "SPRINT_" + (operation == "CREATE" ? "CREATED" : operation == "UPDATE" ? "UPDATED" : operation == "DELETE" ? "DELETED" : operation == "START" ? "STARTED" : "CLOSED"));
        if (operation is "CREATE" or "CLOSE")
        {
            var recipients = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId != UserId && m.User.Status == 1)
                .Select(m => m.UserId).ToListAsync(ct);
            foreach (var recipient in recipients) db.Notifications.Add(new Notification
            {
                UserId = recipient, Type = operation == "CREATE" ? "SPRINT_CREATED" : "SPRINT_CLOSED",
                Title = operation == "CREATE" ? "Sprint mới" : "Sprint đã đóng", Content = sprint.SprintName,
                RefType = "SPRINT", RefId = sprint.SprintId, CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(id.HasValue ? 200 : 201, "Thao tác Sprint thành công.", Details(sprint));
    }

    [HttpPost("{sprintId:long}/backlog-items")]
    public async Task<IActionResult> AddItems(long sprintId, SprintBacklogRequest request, CancellationToken ct)
    {
        if (sprintId <= 0 || request.PbiIds.Any(x => x <= 0) || request.PbiIds.Distinct().Count() != request.PbiIds.Count)
            return Reply(400, "Mã Sprint/PBI không hợp lệ hoặc trùng lặp.");
        var projectId = await db.Sprints.Where(s => s.SprintId == sprintId).Select(s => (long?)s.ProjectId).SingleOrDefaultAsync(ct);
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy Sprint.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return Denied(access);
        var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.SprintId == sprintId, ct);
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        if (sprint.Status == "CLOSED") return Reply(409, "Sprint đã đóng.");
        var items = await db.ProductBacklogItems.Where(p => request.PbiIds.Contains(p.Pbiid) && p.Epic.ProjectId == projectId).ToListAsync(ct);
        if (items.Count != request.PbiIds.Count) return Reply(404, "Có PBI không tồn tại hoặc không thuộc dự án.");
        if (items.Any(p => p.Status is "DONE" or "IN_SPRINT") || await db.SprintBacklogItems.AnyAsync(b =>
            request.PbiIds.Contains(b.Pbiid) && (b.SprintId == sprintId || b.Sprint.Status != "CLOSED"), ct))
            return Reply(409, "Có PBI đã hoàn thành hoặc đang nằm trong Sprint chưa đóng.");
        foreach (var item in items)
        {
            item.Status = "IN_SPRINT"; item.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        foreach (var item in items) db.SprintBacklogItems.Add(new SprintBacklogItem
        { SprintId = sprintId, Pbiid = item.Pbiid, AddedAt = DateTime.UtcNow });
        Log(projectId.Value, sprintId, "SPRINT_BACKLOG_ADDED");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reply(200, "Chọn PBI vào Sprint thành công.", new { sprintId, request.PbiIds });
    }

    [HttpDelete("{sprintId:long}/backlog-items/{pbiId:long}")]
    public async Task<IActionResult> RemoveItem(long sprintId, long pbiId, CancellationToken ct)
    {
        if (sprintId <= 0 || pbiId <= 0) return Reply(400, "Mã không hợp lệ.");
        var projectId = await db.Sprints.Where(s => s.SprintId == sprintId).Select(s => (long?)s.ProjectId).SingleOrDefaultAsync(ct);
        if (!projectId.HasValue) return Reply(404, "Không tìm thấy Sprint.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var access = await Access(projectId.Value, true, ct);
        if (access != 200) return Denied(access);
        var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.SprintId == sprintId, ct);
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        if (sprint.Status != "PLANNED") return Reply(409, "Chỉ loại PBI khỏi Sprint dự kiến.");
        var link = await db.SprintBacklogItems.Include(b => b.Pbi).SingleOrDefaultAsync(b => b.SprintId == sprintId && b.Pbiid == pbiId, ct);
        if (link is null) return Reply(404, "PBI không thuộc Sprint.");
        db.SprintBacklogItems.Remove(link);
        await db.SaveChangesAsync(ct);
        if (link.Pbi.Status == "IN_SPRINT") { link.Pbi.Status = "READY"; link.Pbi.UpdatedAt = DateTime.UtcNow; }
        Log(projectId.Value, sprintId, "SPRINT_BACKLOG_REMOVED");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Reply(200, "Loại PBI khỏi Sprint thành công.");
    }

    [HttpGet("{sprintId:long}/progress")]
    [HttpGet("{sprintId:long}/backlog-items")]
    public async Task<IActionResult> Progress(long sprintId, CancellationToken ct)
    {
        if (sprintId <= 0) return Reply(400, "Mã Sprint không hợp lệ.");
        var sprint = await db.Sprints.AsNoTracking().SingleOrDefaultAsync(s => s.SprintId == sprintId, ct);
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        var access = await Access(sprint.ProjectId, false, ct);
        if (access != 200) return Denied(access);
        var items = await db.SprintBacklogItems.Where(b => b.SprintId == sprintId).Select(b => new
        {
            pbiId = b.Pbiid, b.Pbi.Title,
            storyPoint = sprint.Status == "CLOSED" ? b.StoryPointAtClose ?? b.Pbi.StoryPoint : b.Pbi.StoryPoint,
            status = sprint.Status == "CLOSED" ? b.StatusAtClose ?? b.Pbi.Status : b.Pbi.Status
        }).ToListAsync(ct);
        var totalPoints = items.Sum(x => (long)x.storyPoint);
        var donePoints = items.Where(x => x.status == "DONE").Sum(x => (long)x.storyPoint);
        var taskStatuses = await db.Tasks.Where(t => t.SprintId == sprintId).Select(t => t.Status).ToListAsync(ct);
        return Reply(200, "Lấy tiến độ Sprint thành công.", new
        {
            sprint = Details(sprint), items, totalPbis = items.Count, completedPbis = items.Count(x => x.status == "DONE"),
            totalStoryPoints = totalPoints, completedStoryPoints = donePoints, remainingStoryPoints = totalPoints - donePoints,
            progressPercent = totalPoints == 0 ? 0 : Math.Round(100m * donePoints / totalPoints, 2),
            totalTasks = taskStatuses.Count, completedTasks = taskStatuses.Count(s => s == "DONE")
        });
    }

    [HttpGet("{sprintId:long}/burndown")]
    public async Task<IActionResult> Burndown(long sprintId, CancellationToken ct)
    {
        if (sprintId <= 0) return Reply(400, "Mã Sprint không hợp lệ.");
        var sprint = await db.Sprints.AsNoTracking().SingleOrDefaultAsync(s => s.SprintId == sprintId, ct);
        if (sprint is null) return Reply(404, "Không tìm thấy Sprint.");
        var access = await Access(sprint.ProjectId, false, ct);
        if (access != 200) return Denied(access);
        if (sprint.EndDate == DateOnly.MaxValue || sprint.EndDate.DayNumber - sprint.StartDate.DayNumber > 365)
            return Reply(409, "Sprint cũ quá dài để dựng biểu đồ ngày; giới hạn 366 ngày.");
        var events = await db.Database.SqlQuery<BurndownEvent>($"""
            SELECT DeltaPoints, DeltaRemaining, OccurredAt, IsBaseline
            FROM dbo.SprintBurndownEvent WHERE SprintId={sprintId}
            """).ToListAsync(ct);
        var baselineDate = events.Where(e => e.IsBaseline).Select(e => (DateOnly?)DateOnly.FromDateTime(e.OccurredAt)).Min();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var scope = events.Sum(e => (long)e.DeltaPoints);
        var days = sprint.EndDate.DayNumber - sprint.StartDate.DayNumber + 1;
        var points = new List<object>();
        for (var index = 0; index <= days; index++)
        {
            var day = sprint.StartDate.AddDays(index);
            var dayEvents = events.Where(e => DateOnly.FromDateTime(e.OccurredAt) <= day);
            long? remaining = day > today || (baselineDate.HasValue && day < baselineDate) ? null : dayEvents.Sum(e => (long)e.DeltaRemaining);
            points.Add(new { date = day, idealRemaining = Math.Round(scope * (days - index) / (decimal)days, 2), actualRemaining = remaining });
        }
        return Reply(200, "Lấy dữ liệu Burndown thành công.", new
        {
            sprintId, unit = "storyPoints", totalScope = scope,
            historyAvailableFrom = baselineDate, points,
            note = "Số thực tế là cuối ngày UTC; ngày tương lai/chưa có lịch sử là null. Đường lý tưởng dùng scope hiện tại."
        });
    }

    public class BurndownEvent
    {
        public int DeltaPoints { get; set; }
        public int DeltaRemaining { get; set; }
        public DateTime OccurredAt { get; set; }
        public bool IsBaseline { get; set; }
    }
    private void Log(long projectId, long sprintId, string action) => db.ActivityLogs.Add(new ActivityLog
    { ProjectId = projectId, UserId = UserId, EntityType = "SPRINT", EntityId = sprintId, Action = action, Description = action, CreatedAt = DateTime.UtcNow });
}
