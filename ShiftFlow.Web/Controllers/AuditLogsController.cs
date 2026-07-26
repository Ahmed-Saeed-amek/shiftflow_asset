using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.AuditLogView)]
public class AuditLogsController : Controller
{
    private readonly ApplicationDbContext _db;
    public AuditLogsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var logs = await _db.AuditLogs.Include(l => l.User).OrderByDescending(l => l.CreatedDate).Take(50).ToListAsync();
        return View(logs);
    }
}
