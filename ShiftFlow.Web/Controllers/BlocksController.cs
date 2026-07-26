using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;

namespace ShiftFlow.Web.Controllers;

/// <summary>Blocks are fixed reference data (every block number under each Area is seeded up front — see DbSeeder), not admin-manageable. This controller is read-only.</summary>
[Authorize]
public class BlocksController : Controller
{
    private readonly ApplicationDbContext _db;
    public BlocksController(ApplicationDbContext db) => _db = db;

    /// <summary>Cascading dropdown data source for Zone/Asset forms.</summary>
    [Authorize(Policy = PermissionCatalog.AssetView)]
    public async Task<IActionResult> ByArea(int areaId)
    {
        // Plain number, not a "Block N" label — the field's own <label> already says "Block"/"القطعة",
        // and a hardcoded English prefix here would show through even when the UI is in Arabic.
        var blocks = await _db.Blocks.Where(b => b.AreaId == areaId)
            .OrderBy(b => b.Number).Select(b => new { b.Id, Name = b.Number.ToString() }).ToListAsync();
        return Json(blocks);
    }
}
