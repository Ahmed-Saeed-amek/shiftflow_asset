using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Services;

namespace ShiftFlow.Web.Controllers.Api;

/// <summary>Typeahead search for the shared employee picker. Base route: /api/users</summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public UsersApiController(ApplicationDbContext db) => _db = db;

    /// <summary>GET /api/users/search?q=&amp;role=&amp;activeOnly=true — up to 20 matching employees.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, string? role, bool activeOnly = true)
    {
        var users = _db.Users.AsNoTracking().AsQueryable();
        if (activeOnly) users = users.Where(u => u.IsActive);

        // This endpoint backs employee-facing pickers used to assign field/maintenance work
        // (inspection orders, maintenance orders, work orders) app-wide — vendor portal accounts
        // (a separate account type), HR, and Admin (IT Administration, no field-work role) must
        // never appear here. Without this, e.g. a Work Order's "Reassign Employee" picker could
        // bind an HR or System Administrator account to a maintenance task with no warning, and
        // any authenticated caller (this endpoint has no role restriction beyond [Authorize]) could
        // enumerate admin accounts and their emails via the search.
        var excludedRoles = new[] { "Vendor", "HR", "Admin" };
        users = users.Where(u => !_db.UserRoles.Any(ur => ur.UserId == u.Id &&
            _db.Roles.Any(r => r.Id == ur.RoleId && excludedRoles.Contains(r.Name))));

        if (!string.IsNullOrWhiteSpace(role))
        {
            users = users.Where(u => _db.UserRoles
                .Any(ur => ur.UserId == u.Id &&
                           _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = SearchQuery.Cap(q.Trim())!;
            users = users.Where(u =>
                u.FullName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.EmployeeNumber != null && u.EmployeeNumber.Contains(term)));
        }

        var result = await users
            .OrderBy(u => u.FullName)
            .Take(20)
            .Select(u => new
            {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email,
                employeeNumber = u.EmployeeNumber,
                department = u.Department,
            })
            .ToListAsync();

        return Ok(result);
    }
}
