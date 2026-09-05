using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Services;

/// <summary>Preserves a submitted Report/Submit Fix form's typed values across the
/// redirect-on-error round trip (e.g. a rejected attachment) so the vendor/employee doesn't have
/// to reselect the parts they'd already entered — only the file input (browsers won't let JS
/// repopulate that) and, currently, the completion date specifically still need re-entering:
/// confirmed via direct instrumentation that the parsed DateTime is correct going into TempData
/// and immediately readable back out in the same request, yet comes back empty once actually
/// rendered after the redirect, while every other stashed key in the same dictionary survives
/// that same round trip — an isolated TempData-persistence quirk for this one value that a
/// deeper dive didn't resolve.</summary>
public static class FixFormRetainer
{
    public static DateTime? ParseCompletionDate(IFormCollection form) =>
        DateTime.TryParse(form["CompletionDate"], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    public static void Stash(ITempDataDictionary tempData, VendorFixViewModel vm, DateTime? completionDate)
    {
        tempData["FixForm_CDate"] = completionDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        tempData["FixForm_SparePartIds"] = System.Text.Json.JsonSerializer.Serialize(vm.SparePartIds ?? []);
        tempData["FixForm_PartQuantities"] = System.Text.Json.JsonSerializer.Serialize(vm.PartQuantities ?? []);
    }
}
