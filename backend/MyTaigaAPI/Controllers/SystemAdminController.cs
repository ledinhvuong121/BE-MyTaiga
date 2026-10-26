using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;
using MyTaigaAPI.Services.Auth;
namespace MyTaigaAPI.Controllers;
public class SystemRoleRequest{[Required,RegularExpression("^(ADMIN|USER)$")]public string Role {get;set;}="USER";}
public class ConfigRequest{[Required,MinLength(1)]public Dictionary<string,string> Values {get;set;}=[];}
[ApiController,Authorize(Roles="ADMIN"),Route("api/v1/admin")]
public class SystemAdminController(ScrumbanDbContext db,RegisterService registration):ControllerBase
{
    long Me=>long.Parse(User.FindFirst("sub")!.Value);
    ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    void Log(string action,long? target,string description)=>db.ActivityLogs.Add(new(){UserId=Me,Action=action,EntityType="SYSTEM",EntityId=target,Description=description,CreatedAt=DateTime.UtcNow});
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery,StringLength(200)]string? keyword=null,[FromQuery,Range(0,1)]byte? status=null,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var query=db.Users.AsNoTracking().AsQueryable();if(!string.IsNullOrWhiteSpace(keyword)){keyword=keyword.Trim();query=query.Where(u=>u.FullName.Contains(keyword)||u.Email.Contains(keyword));}if(status.HasValue)query=query.Where(u=>u.Status==status);
        var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return BadRequest();var total=await query.CountAsync(ct);
        var rows=await query.OrderBy(u=>u.UserId).Skip((int)skip).Take(pageSize).Select(u=>new{u.UserId,u.FullName,u.Email,u.Status,systemRole=u.UserRole!.Role.RoleName,u.CreatedAt}).ToListAsync(ct);
        return Reply(200,"Lấy người dùng thành công.",new{items=rows,total,page,pageSize});
    }
    [HttpPost("users")]
    public async Task<IActionResult> Create(RegisterRequest request,CancellationToken ct)
    {
        var result=await registration.RegisterAsync(request,ct,Me);
        if(result.StatusCode!=201)return Reply(result.StatusCode,result.Message);

        return Reply(201,"Tạo người dùng thành công.",new{userId=result.UserId,systemRole="USER"});
    }
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)=>Reply(200,"Lấy vai trò hệ thống thành công.",await db.Roles.Where(r=>r.Scope=="SYSTEM").Select(r=>new{r.RoleId,r.RoleName,r.Scope,r.Description}).ToListAsync(ct));
    [HttpPut("users/{id:long}/role")]
    public async Task<IActionResult> Role(long id,SystemRoleRequest request,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var roles=await db.UserRoles.Include(r=>r.Role).Where(r=>r.Role.Scope=="SYSTEM").ToListAsync(ct);
        var current=roles.SingleOrDefault(r=>r.UserId==id);if(current is null)return NotFound();
        if(current.Role.RoleName==request.Role)return Reply(200,"Vai trò không thay đổi.",new{userId=id,systemRole=request.Role});
        // Chương 2 requires exactly one Admin. Transfer is deliberately not implicit.
        if(request.Role=="ADMIN"&&roles.Any(r=>r.Role.RoleName=="ADMIN"))return Reply(409,"Hệ thống chỉ có một Admin. Không thể tạo Admin thứ hai.");
        if(current.Role.RoleName=="ADMIN")return Reply(409,"Không được gỡ vai trò Admin duy nhất.");
        var role=await db.Roles.SingleAsync(r=>r.Scope=="SYSTEM"&&r.RoleName==request.Role,ct);current.RoleId=role.RoleId;
        await db.Users.Where(u=>u.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.TokenVersion,u=>u.TokenVersion+1),ct);
        Log("SYSTEM_ROLE_CHANGED",id,"Đổi vai trò hệ thống; thu hồi phiên cũ.");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Reply(200,"Cập nhật vai trò thành công.");
    }
    [HttpGet("system-config")]
    public async Task<IActionResult> Config(CancellationToken ct)=>Reply(200,"Lấy cấu hình thành công.",await db.SystemConfigs.AsNoTracking().OrderBy(c=>c.ConfigKey).ToListAsync(ct));
    static readonly Dictionary<string,(int Min,int Max)> Bounds=new(){["MAX_FAILED_LOGIN"]=(1,20),["MIN_PASSWORD_LENGTH"]=(8,64),["RESET_TOKEN_EXPIRE_MINUTES"]=(1,1440),["JWT_EXPIRE_MINUTES"]=(1,10080),["MAX_FILE_SIZE_MB"]=(1,25),["MAX_COMMENT_LENGTH"]=(1,5000),["DEADLINE_WARNING_HOURS"]=(1,720),["DEFAULT_SPRINT_WEEKS"]=(1,12)};
    [HttpPut("system-config")]
    public async Task<IActionResult> Configure(ConfigRequest request,CancellationToken ct)
    {
        if(request.Values is null||request.Values.Count is <1 or >9)return BadRequest();var validated=new Dictionary<string,string>();
        foreach(var (key,value) in request.Values)
        {
            if(value is null)return BadRequest();
            if(key=="ALLOWED_FILE_EXTENSIONS")
            {
                var extensions=value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Select(e=>e.ToLowerInvariant()).ToArray();
                if(extensions.Length==0||extensions.Any(e=>!TaskFileStore.MimeTypes.ContainsKey(e)))return Reply(400,"Đuôi tệp không được hỗ trợ.");validated[key]=string.Join(',',extensions);
            }
            else if(Bounds.TryGetValue(key,out var range)&&int.TryParse(value,out var number)&&number>=range.Min&&number<=range.Max)validated[key]=number.ToString();
            else return Reply(400,"Khóa cấu hình hoặc giá trị không hợp lệ: "+key);
        }
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var rows=await db.SystemConfigs.Where(c=>validated.Keys.Contains(c.ConfigKey)).ToListAsync(ct);if(rows.Count!=validated.Count)return BadRequest();
        foreach(var row in rows){row.ConfigValue=validated[row.ConfigKey];row.UpdatedAt=DateTime.UtcNow;}
        Log("SYSTEM_CONFIG_UPDATED",null,"Cập nhật các khóa: "+string.Join(",",validated.Keys));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Reply(200,"Cập nhật cấu hình thành công.");
    }
    [HttpGet("activity-logs")]
    public async Task<IActionResult> History([FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return BadRequest();var query=db.ActivityLogs.AsNoTracking().Where(l=>l.ProjectId==null);
        return Reply(200,"Lấy nhật ký hệ thống thành công.",new{items=await query.OrderByDescending(l=>l.ActivityLogId).Skip((int)skip).Take(pageSize).ToListAsync(ct),total=await query.CountAsync(ct),page,pageSize});
    }
}
[ApiController,Authorize]
public class PermissionsController(ScrumbanDbContext db):ControllerBase
{
    [HttpGet("api/v1/me/permissions")]
    public async Task<IActionResult> Get([FromQuery,Range(1,long.MaxValue)]long? projectId=null,CancellationToken ct=default)
    {
        var me=long.Parse(User.FindFirst("sub")!.Value);var system=await db.UserRoles.AsNoTracking().Include(u=>u.Role).ThenInclude(r=>r.Permissions).SingleAsync(u=>u.UserId==me,ct);
        ProjectMember? member=null;
        if(projectId.HasValue)
        {
            member=await db.ProjectMembers.AsNoTracking().Include(m=>m.Role).ThenInclude(r=>r.Permissions).SingleOrDefaultAsync(m=>m.ProjectId==projectId&&m.UserId==me&&m.Project.Status!="DELETED",ct);
            if(member is null)return StatusCode(403);
        }
        return Ok(new{success=true,message="Lấy quyền thành công.",data=new{systemRole=system.Role.RoleName,systemPermissions=system.Role.Permissions.Select(p=>p.PermissionCode).Order(),projectId,projectRole=member?.Role.RoleName,projectPermissions=member?.Role.Permissions.Select(p=>p.PermissionCode).Order().ToArray()??[]}});
    }
}
