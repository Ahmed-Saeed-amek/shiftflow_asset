using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Controllers;

[Authorize]
public class ContractsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IContractService _contractService;
    private readonly UserManager<ApplicationUser> _userManager;
    public ContractsController(ApplicationDbContext db, IContractService contractService, UserManager<ApplicationUser> userManager)
    {
        _db = db; _contractService = contractService; _userManager = userManager;
    }

    [Authorize(Policy = PermissionCatalog.ContractView)]
    public async Task<IActionResult> Index()
    {
        var contracts = await _db.Contracts.Include(c => c.Vendor).Include(c => c.AssetLinks)
            .OrderByDescending(c => c.StartDate).ToListAsync();
        return View(contracts);
    }

    [Authorize(Policy = PermissionCatalog.ContractView)]
    public async Task<IActionResult> Details(int id)
    {
        var contract = await _db.Contracts.Include(c => c.Vendor)
            .Include(c => c.AssetLinks).ThenInclude(l => l.Asset)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (contract == null) return NotFound();
        if (contract.ContractType == "Preventive Maintenance")
            ViewBag.PmSchedule = await _contractService.GetPreventiveMaintenanceScheduleAsync(id);
        return View(contract);
    }

    [Authorize(Policy = PermissionCatalog.ContractManage)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        ViewBag.SelectedAssetChips = new List<AssetChip>();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new ContractViewModel());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.ContractManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        try
        {
            await _contractService.CreateAsync(new Contract
            {
                VendorId = vm.VendorId, ContractType = vm.ContractType, ContractNumber = vm.ContractNumber,
                StartDate = vm.StartDate, EndDate = vm.EndDate, Cost = vm.Cost, Notes = vm.Notes,
                PmCadence = vm.ContractType == "Preventive Maintenance" ? vm.PmCadence : null,
            }, vm.AssetIds ?? [], userId);
            TempData["Success"] = "Contract created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync();
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            return View(vm);
        }
    }

    [Authorize(Policy = PermissionCatalog.ContractManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var contract = await _db.Contracts.Include(c => c.AssetLinks).ThenInclude(l => l.Asset).FirstOrDefaultAsync(c => c.Id == id);
        if (contract == null) return NotFound();
        await PopulateLookupsAsync();
        ViewBag.SelectedAssetChips = contract.AssetLinks
            .Select(l => new AssetChip { Id = l.AssetId, Label = $"{l.Asset!.AssetTag} — {l.Asset.Name}" }).ToList();
        ViewBag.ReturnUrl = Url.IsLocalUrl(Request.Headers.Referer.ToString()) ? Request.Headers.Referer.ToString() : Url.Action("Index");
        return View(new ContractViewModel
        {
            Id = contract.Id, VendorId = contract.VendorId, ContractType = contract.ContractType, ContractNumber = contract.ContractNumber,
            StartDate = contract.StartDate, EndDate = contract.EndDate, Cost = contract.Cost, Notes = contract.Notes,
            AssetIds = contract.AssetLinks.Select(l => l.AssetId).ToList(),
            PmCadence = contract.PmCadence,
        });
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.ContractManage), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ContractViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds); return View(vm); }
        var userId = _userManager.GetUserId(User)!;
        try
        {
            await _contractService.UpdateAsync(new Contract
            {
                Id = vm.Id, VendorId = vm.VendorId, ContractType = vm.ContractType, ContractNumber = vm.ContractNumber,
                StartDate = vm.StartDate, EndDate = vm.EndDate, Cost = vm.Cost, Notes = vm.Notes,
                PmCadence = vm.ContractType == "Preventive Maintenance" ? vm.PmCadence : null,
            }, vm.AssetIds ?? [], userId);
            TempData["Success"] = "Contract updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync();
            ViewBag.SelectedAssetChips = await BuildChipsAsync(vm.AssetIds);
            return View(vm);
        }
    }

    private async Task<List<AssetChip>> BuildChipsAsync(List<int>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) return [];
        return await _db.Assets.Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetChip { Id = a.Id, Label = a.AssetTag + " — " + a.Name }).ToListAsync();
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Vendors = await _db.Vendors.Where(v => v.Status == "Active").OrderBy(v => v.Name).ToListAsync();
        ViewBag.Categories = await _db.AssetCategories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.Name).ToListAsync();
    }
}
