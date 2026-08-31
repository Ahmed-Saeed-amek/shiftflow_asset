using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class TeamsController : Controller
{
    private readonly ITeamService _teams;
    private readonly UserManager<ApplicationUser> _um;

    public TeamsController(ITeamService teams, UserManager<ApplicationUser> um)
    {
        _teams = teams;
        _um = um;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [Authorize(Policy = PermissionCatalog.TeamView)]
    public async Task<IActionResult> Index()
    {
        var teams = await _teams.GetAllAsync(includeInactive: true);
        return View(teams);
    }

    [Authorize(Policy = PermissionCatalog.TeamView)]
    public async Task<IActionResult> Details(int id)
    {
        var team = await _teams.GetByIdAsync(id);
        if (team == null) return NotFound();
        return View(team);
    }

    [Authorize(Policy = PermissionCatalog.TeamManage)]
    public IActionResult Create() => View(new TeamCreateVm());

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.TeamManage)]
    public async Task<IActionResult> Create(TeamCreateVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            var team = await _teams.CreateAsync(vm.Name, vm.NameAr, vm.Description, vm.MemberUserIds, CurrentUserId);
            TempData["Success"] = $"Team \"{team.Name}\" created.";
            return RedirectToAction(nameof(Details), new { id = team.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    [Authorize(Policy = PermissionCatalog.TeamManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var team = await _teams.GetByIdAsync(id);
        if (team == null) return NotFound();
        ViewBag.CurrentMembers = team.Members.Select(m => new TeamMemberChip { UserId = m.UserId, Label = m.User.FullName }).ToList();
        return View(new TeamEditVm
        {
            Id = team.Id, Name = team.Name, NameAr = team.NameAr, Description = team.Description, IsActive = team.IsActive,
            MemberUserIds = team.Members.Select(m => m.UserId).ToList(),
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.TeamManage)]
    public async Task<IActionResult> Edit(int id, TeamEditVm vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) { await PopulateCurrentMembersAsync(vm); return View(vm); }

        try
        {
            await _teams.UpdateAsync(id, vm.Name, vm.NameAr, vm.Description, CurrentUserId);
            await _teams.SetActiveAsync(id, vm.IsActive, CurrentUserId);
            await _teams.SetMembersAsync(id, vm.MemberUserIds ?? new(), CurrentUserId);
            TempData["Success"] = "Team updated.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateCurrentMembersAsync(vm);
            return View(vm);
        }
        catch (DbUpdateException)
        {
            // A stale member id (e.g. the user was deleted mid-edit) fails at the FK level —
            // surface it the same friendly way as elsewhere rather than the generic 500 page.
            ModelState.AddModelError("", "Could not save membership — one of the selected users may no longer exist.");
            await PopulateCurrentMembersAsync(vm);
            return View(vm);
        }
    }

    // Edit.cshtml's chip picker always reads ViewBag.CurrentMembers (@foreach with no null
    // check) — every redisplay path above must populate it or the view throws a
    // NullReferenceException instead of showing the validation error. Rebuilt from the
    // submitted MemberUserIds (not re-fetched from the team) so the user's in-progress
    // selection survives the redisplay, same as the rest of the form's fields already do.
    private async Task PopulateCurrentMembersAsync(TeamEditVm vm)
    {
        var ids = vm.MemberUserIds ?? new();
        var users = await _um.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        ViewBag.CurrentMembers = ids
            .Select(id => users.FirstOrDefault(u => u.Id == id))
            .Where(u => u != null)
            .Select(u => new TeamMemberChip { UserId = u!.Id, Label = u.FullName })
            .ToList();
    }
}
