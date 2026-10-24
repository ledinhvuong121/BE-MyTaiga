using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ScrumbanDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ScrumbanDb")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình ConnectionStrings:ScrumbanDb.")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();