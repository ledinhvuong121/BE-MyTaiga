using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}/flow-metrics")]
public class FlowMetricsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) =>
        StatusCode(code, new { success = code < 400, message, data });

    private async Task<int> Access(long projectId, long? sprintId, CancellationToken ct)
    {
        if (projectId <= 0 || sprintId is <= 0) return 400;
        if (!await db.Projects.AnyAsync(p => p.ProjectId == projectId && p.Status != "DELETED", ct)) return 404;
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == UserId && m.Role.RoleName == "MANAGER", ct)) return 403;
        if (sprintId.HasValue && !await db.Sprints.AnyAsync(s => s.ProjectId == projectId && s.SprintId == sprintId, ct)) return 404;
        return 200;
    }
    private ObjectResult Denied(int code) => Reply(code, code == 400 ? "Mã dự án/Sprint không hợp lệ." :
        code == 403 ? "Chỉ Manager của dự án được xem Flow Metrics." : "Không tìm thấy dự án/Sprint trong phạm vi yêu cầu.");
    private static decimal Days(TimeSpan value) => Math.Round((decimal)value.TotalDays, 4);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private async Task<DateTime> HistoryStart(CancellationToken ct) => Utc(await db.Database.SqlQuery<DateTime>($"""
        SELECT StartedAt AS Value FROM dbo.TaskCompletionHistoryMetadata WHERE Id=1
        """).SingleAsync(ct));

    [HttpGet("wip")]
    public async Task<IActionResult> Wip(long projectId, [FromQuery] long? sprintId, CancellationToken ct)
    {
        var access = await Access(projectId, sprintId, ct); if (access != 200) return Denied(access);
        var query = db.Tasks.Where(t => t.ProjectId == projectId && (t.Status == "IN_PROGRESS" || t.Status == "IN_REVIEW"));
        if (sprintId.HasValue) query = query.Where(t => t.SprintId == sprintId);
        var groups = await query.GroupBy(t => t.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
        return Reply(200, "Lấy WIP hiện tại thành công.", new
        {
            projectId, sprintId, wip = groups.Sum(g => g.count),
            inProgress = groups.FirstOrDefault(g => g.status == "IN_PROGRESS")?.count ?? 0,
            inReview = groups.FirstOrDefault(g => g.status == "IN_REVIEW")?.count ?? 0,
            asOfUtc = DateTime.UtcNow
        });
    }

    private static (DateTime? Start, DateTime? End) Period(DateOnly? from, DateOnly? to, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var first = from ?? today.AddDays(-6); var last = to ?? today;
        if (first > last || last > today || last == DateOnly.MaxValue || last.DayNumber - first.DayNumber > 365)
            return (null, null);
        return (first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    [HttpGet("throughput")]
    public async Task<IActionResult> Throughput(long projectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] long? sprintId, CancellationToken ct)
    {
        var access = await Access(projectId, sprintId, ct); if (access != 200) return Denied(access);
        var now = DateTime.UtcNow; var period = Period(from, to, now);
        if (!period.Start.HasValue) return Reply(400, "Khoảng ngày tối đa 366 ngày, từ ngày không sau đến ngày, không có ngày tương lai.");
        var query = db.TaskCompletionHistories.AsNoTracking().Where(h => h.ProjectId == projectId &&
            h.CompletedAt >= period.Start && h.CompletedAt < period.End && h.CompletedAt <= now);
        if (sprintId.HasValue) query = query.Where(h => h.SprintId == sprintId);
        var groups = await query.GroupBy(h => h.CompletedAt.Date).Select(g => new { date = g.Key, count = g.Count() }).ToListAsync(ct);
        var imported = await query.CountAsync(h => h.IsBaseline, ct);
        var historyStarted = await HistoryStart(ct);
        var points = new List<object>();
        for (var day = period.Start.Value; day < period.End; day = day.AddDays(1))
            points.Add(new { date = DateOnly.FromDateTime(day), doneTasks = groups.FirstOrDefault(g => g.date == day)?.count ?? 0,
                historyComplete = day >= historyStarted });
        return Reply(200, "Lấy Throughput thành công.", new
        {
            projectId, sprintId, from = DateOnly.FromDateTime(period.Start.Value), to = DateOnly.FromDateTime(period.End!.Value.AddDays(-1)),
            totalDoneTasks = groups.Sum(g => g.count), points, importedBaselineCount = imported,
            historyTrackingStartedAtUtc = historyStarted, hasPartialHistoricalCoverage = period.Start < historyStarted,
            definition = "Mỗi Task được tính một lần tại lần hoàn thành đầu tiên, kể cả sau khi mở lại hoặc xóa.", asOfUtc = now
        });
    }

    [HttpGet("throughput/sprints")]
    public async Task<IActionResult> Compare(long projectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        var access = await Access(projectId, null, ct); if (access != 200) return Denied(access);
        var now = DateTime.UtcNow; var period = Period(from, to, now);
        if (!period.Start.HasValue) return Reply(400, "Khoảng ngày không hợp lệ (tối đa 366 ngày, không tương lai).");
        var skip = ((long)page - 1) * pageSize; if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var query = db.Sprints.AsNoTracking().Where(s => s.ProjectId == projectId);
        var total = await query.CountAsync(ct);
        var historyStarted = await HistoryStart(ct);
        var items = await query.OrderBy(s => s.StartDate).ThenBy(s => s.SprintId).Skip((int)skip).Take(pageSize).Select(s => new
        {
            s.SprintId, s.SprintName, s.Status, s.StartDate, s.EndDate,
            doneTasks = db.TaskCompletionHistories.Count(h => h.ProjectId == projectId && h.SprintId == s.SprintId &&
                h.CompletedAt >= period.Start && h.CompletedAt < period.End && h.CompletedAt <= now)
        }).ToListAsync(ct);
        return Reply(200, "So sánh Throughput giữa các Sprint thành công.", new
        { items, total, page, pageSize, historyTrackingStartedAtUtc = historyStarted,
            hasPartialHistoricalCoverage = period.Start < historyStarted, asOfUtc = now });
    }

    [HttpGet("cycle-time")]
    public async Task<IActionResult> Cycle(long projectId, [FromQuery] long? sprintId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
    {
        var access = await Access(projectId, sprintId, ct); if (access != 200) return Denied(access);
        var now = DateTime.UtcNow;
        var query = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId && t.Status == "DONE");
        if (sprintId.HasValue) query = query.Where(t => t.SprintId == sprintId);
        var total = await query.CountAsync(ct); var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderByDescending(t => t.CompletedAt).ThenBy(t => t.TaskId).Skip((int)skip).Take(pageSize)
            .Select(t => new { t.TaskId, t.Title, t.SprintId, t.StartedAt, t.CompletedAt }).ToListAsync(ct);
        return Reply(200, "Lấy Cycle Time thành công.", new
        {
            items = rows.Select(t => new
            {
                t.TaskId, t.Title, t.SprintId, startedAt = t.StartedAt.HasValue ? Utc(t.StartedAt.Value) : (DateTime?)null,
                completedAt = t.CompletedAt.HasValue ? Utc(t.CompletedAt.Value) : (DateTime?)null,
                cycleTimeDays = t.StartedAt.HasValue && t.CompletedAt >= t.StartedAt && t.CompletedAt <= now
                    ? Days(t.CompletedAt.Value - t.StartedAt.Value) : (decimal?)null
            }), total, page, pageSize, asOfUtc = now
        });
    }

    [HttpGet("cycle-time/average")]
    public async Task<IActionResult> CycleAverage(long projectId, [FromQuery] long? sprintId, CancellationToken ct)
    {
        var access = await Access(projectId, sprintId, ct); if (access != 200) return Denied(access);
        var now = DateTime.UtcNow;
        var result = await db.Database.SqlQuery<CycleSummary>($"""
            SELECT COUNT_BIG(*) AS ValidTasks,
                AVG(CAST(DATEDIFF_BIG(SECOND,StartedAt,CompletedAt) AS FLOAT))/86400.0 AS AverageDays
            FROM dbo.Task WHERE ProjectId={projectId} AND ({sprintId} IS NULL OR SprintId={sprintId})
                AND Status='DONE' AND StartedAt IS NOT NULL AND CompletedAt>=StartedAt AND CompletedAt<={now}
            """).SingleAsync(ct);
        var totalDone = await db.Tasks.LongCountAsync(t => t.ProjectId == projectId && t.Status == "DONE" &&
            (!sprintId.HasValue || t.SprintId == sprintId), ct);
        return Reply(200, "Lấy Cycle Time trung bình thành công.", new
        {
            projectId, sprintId, validTasks = result.ValidTasks, skippedInvalidTasks = totalDone - result.ValidTasks,
            averageCycleTimeDays = result.AverageDays.HasValue ? Math.Round((decimal)result.AverageDays.Value, 4) : (decimal?)null,
            asOfUtc = now
        });
    }

    [HttpGet("work-item-age")]
    public async Task<IActionResult> Age(long projectId, [FromQuery] long? sprintId,
        [FromQuery, Range(typeof(decimal), "0", "36500")] decimal minAgeDays = 0,
        [FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var access = await Access(projectId, sprintId, ct); if (access != 200) return Denied(access);
        var now = DateTime.UtcNow; var threshold = now.AddDays(-(double)minAgeDays);
        var query = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId && t.Status != "DONE" &&
            t.StartedAt.HasValue && t.StartedAt <= threshold);
        if (sprintId.HasValue) query = query.Where(t => t.SprintId == sprintId);
        var total = await query.CountAsync(ct); var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return Reply(400, "Trang vượt phạm vi hỗ trợ.");
        var rows = await query.OrderBy(t => t.StartedAt).ThenBy(t => t.TaskId).Skip((int)skip).Take(pageSize)
            .Select(t => new { t.TaskId, t.Title, t.SprintId, t.Status, t.AssigneeId, t.StartedAt }).ToListAsync(ct);
        return Reply(200, "Lấy Work Item Age thành công.", new
        {
            items = rows.Select(t => new { t.TaskId, t.Title, t.SprintId, t.Status, t.AssigneeId,
                startedAt = Utc(t.StartedAt!.Value), workItemAgeDays = Days(now - t.StartedAt.Value) }),
            total, page, pageSize, minAgeDays, asOfUtc = now,
            definition = "Công việc đã bắt đầu nhưng chưa DONE; lọc minAgeDays để phát hiện tồn đọng."
        });
    }

    public class CycleSummary
    {
        public long ValidTasks { get; set; }
        public double? AverageDays { get; set; }
    }
}
