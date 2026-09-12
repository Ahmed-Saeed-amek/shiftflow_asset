using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.AuditLogView)]
public class AuditLogsController : Controller
{
    private readonly ApplicationDbContext _db;
    public AuditLogsController(ApplicationDbContext db) => _db = db;

    private const int PageSize = 50;

    /// <summary>Server-paged and filterable — the list used to be a hard Take(50) with no way to
    /// reach anything older.</summary>
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? entityType, string? userId, string? action, int page = 1)
    {
        if (page < 1) page = 1;

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(l => l.CreatedDate >= from.Value.Date);
        // Inclusive end date: the picker posts a date, the column is a timestamp.
        if (to.HasValue) query = query.Where(l => l.CreatedDate < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(l => l.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(l => l.UserId == userId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages) page = totalPages;

        var logs = await query.Include(l => l.User)
            .OrderByDescending(l => l.CreatedDate)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync();

        return View(new AuditLogIndexViewModel
        {
            Logs = logs,
            From = from, To = to, EntityType = entityType, UserId = userId, Action = action,
            EntityTypes = await _db.AuditLogs.AsNoTracking().Select(l => l.EntityType).Distinct().OrderBy(t => t).ToListAsync(),
            Actions = await _db.AuditLogs.AsNoTracking().Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync(),
            Users = (await _db.AuditLogs.AsNoTracking()
                    .Select(l => new { l.UserId, Name = l.User.FullName })
                    .Distinct().OrderBy(u => u.Name).ToListAsync())
                .Select(u => (u.UserId, u.Name)).ToList(),
            TotalCount = totalCount,
            Pagination = new PaginationModel { Page = page, TotalPages = totalPages },
        });
    }
}
