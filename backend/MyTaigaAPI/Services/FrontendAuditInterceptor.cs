using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyTaigaAPI.Entities;
namespace MyTaigaAPI.Services;
public class FrontendAuditInterceptor(IHttpContextAccessor http):SaveChangesInterceptor
{
    void Capture(DbContext? db)
    {
        if(db is null)return;
        var actor=long.TryParse(http.HttpContext?.User.FindFirst("sub")?.Value,out var id)?id:(long?)null;
        foreach(var row in db.ChangeTracker.Entries<Notification>().Where(e=>e.State==EntityState.Added))
            if(row.Entity.ActorId is null)row.Entity.ActorId=actor;
        var allowed=new HashSet<string>{"Title","Description","Status","Priority","AssigneeId","Deadline","ProgressPercent","Position","WipLimit","RoomName","Color","TagsJson","StoryPoints"};
        foreach(var log in db.ChangeTracker.Entries<ActivityLog>().Where(e=>e.State==EntityState.Added&&e.Entity.ChangesJson==null))
        {
            var entry=db.ChangeTracker.Entries().FirstOrDefault(e=>e.State==EntityState.Modified&&
                e.Metadata.ClrType.Name.ToUpperInvariant().Replace("BOARDCOLUMN","BOARD_COLUMN")==log.Entity.EntityType&&
                e.Properties.Any(p=>p.Metadata.IsPrimaryKey()&&p.CurrentValue is long v&&v==log.Entity.EntityId));
            if(entry is null)continue;
            var changes=entry.Properties.Where(p=>p.IsModified&&allowed.Contains(p.Metadata.Name)&&!Equals(p.OriginalValue,p.CurrentValue))
                .ToDictionary(p=>JsonNamingPolicy.CamelCase.ConvertName(p.Metadata.Name),p=>new{from=p.OriginalValue,to=p.CurrentValue});
            if(changes.Count>0)log.Entity.ChangesJson=JsonSerializer.Serialize(changes);
        }
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData e,InterceptionResult<int> result){Capture(e.Context);return result;}
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default){Capture(e.Context);return ValueTask.FromResult(result);}
}
