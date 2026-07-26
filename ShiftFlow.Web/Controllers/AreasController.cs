using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

/// <summary>Areas are fixed reference data (every real area under each governorate is seeded up front — see DbSeeder.KuwaitAreas), not admin-manageable. This controller is read-only.</summary>
[Authorize]
public class AreasController : Controller
{
    private readonly ApplicationDbContext _db;
    public AreasController(ApplicationDbContext db) => _db = db;

    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> Index()
    {
        var areas = await _db.Areas.Include(a => a.Governorate).OrderBy(a => a.Governorate!.Name).ThenBy(a => a.Name).ToListAsync();
        return View(areas);
    }

    /// <summary>Cascading dropdown data source for Zone/Asset forms.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByGovernorate(int governorateId)
    {
        var areas = await _db.Areas.Where(a => a.GovernorateId == governorateId)
            .OrderBy(a => a.Name).Select(a => new { a.Id, a.Name, a.NameAr }).ToListAsync();
        return Json(areas);
    }
}
