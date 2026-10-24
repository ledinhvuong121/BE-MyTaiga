using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;

namespace MyTaigaAPI.Services;

public class SystemConfigService
{
    private readonly ScrumbanDbContext _db;

    public SystemConfigService(ScrumbanDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetPositiveIntAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var value = await _db.SystemConfigs
            .AsNoTracking()
            .Where(x => x.ConfigKey == key)
            .Select(x => x.ConfigValue)
            .SingleOrDefaultAsync(cancellationToken);

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result)
            || result <= 0)
        {
            throw new InvalidOperationException(
                $"Cấu hình {key} bị thiếu hoặc không phải số nguyên dương.");
        }

        return result;
    }
}