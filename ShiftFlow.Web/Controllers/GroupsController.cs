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
public class GroupsController : Controller
{
    private readonly IGroupService _groups;
    private readonly UserManager<ApplicationUser> _um;

    public GroupsController(IGroupService groups, UserManager<ApplicationUser> um)
    {
        _groups = groups;
        _um = um;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [Authorize(Policy = PermissionCatalog.GroupView)]
    public async Task<IActionResult> Index()
    {
        var groups = await _groups.GetAllAsync(includeInactive: true);
        return View(groups);
    }

    [Authorize(Policy = PermissionCatalog.GroupView)]
    public async Task<IActionResult> Details(int id)
    {
        var group = await _groups.GetByIdAsync(id);
        if (group == null) return NotFound();
        return View(group);
    }

    [Authorize(Policy = PermissionCatalog.GroupManage)]
    public IActionResult Create() => View(new GroupCreateVm());

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.GroupManage)]
    public async Task<IActionResult> Create(GroupCreateVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            var group = await _groups.CreateAsync(vm.Name, vm.NameAr, vm.Description, vm.MemberUserIds, CurrentUserId);
            TempData["Success"] = $"Group \"{group.Name}\" created.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    [Authorize(Policy = PermissionCatalog.GroupManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var group = await _groups.GetByIdAsync(id);
        if (group == null) return NotFound();
        ViewBag.CurrentMembers = group.Members.Select(m => new GroupMemberChip { UserId = m.UserId, Label = m.User.FullName }).ToList();
        var memberIds = group.Members.Select(m => m.UserId).ToList();
        return View(new GroupEditVm
        {
            Id = group.Id, Name = group.Name, NameAr = group.NameAr, Description = group.Description, IsActive = group.IsActive,
            MemberUserIds = memberIds,
            OriginalMemberUserIds = memberIds,
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = PermissionCatalog.GroupManage)]
    public async Task<IActionResult> Edit(int id, GroupEditVm vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) { await PopulateCurrentMembersAsync(vm); return View(vm); }

        try
        {
            await _groups.UpdateAsync(id, vm.Name, vm.NameAr, vm.Description, CurrentUserId);
            await _groups.SetActiveAsync(id, vm.IsActive, CurrentUserId);
            await _groups.SetMembersAsync(id, vm.MemberUserIds ?? new(), vm.OriginalMemberUserIds ?? new(), CurrentUserId);
            TempData["Success"] = "Group updated.";
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
    // submitted MemberUserIds (not re-fetched from the group) so the user's in-progress
    // selection survives the redisplay, same as the rest of the form's fields already do.
    private async Task PopulateCurrentMembersAsync(GroupEditVm vm)
    {
        var ids = vm.MemberUserIds ?? new();
        var users = await _um.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        ViewBag.CurrentMembers = ids
            .Select(id => users.FirstOrDefault(u => u.Id == id))
            .Where(u => u != null)
            .Select(u => new GroupMemberChip { UserId = u!.Id, Label = u.FullName })
            .ToList();
    }
}
