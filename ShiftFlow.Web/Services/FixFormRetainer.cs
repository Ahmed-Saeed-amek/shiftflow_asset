using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ShiftFlow.Web.ViewModels;

namespace ShiftFlow.Web.Services;

/// <summary>Preserves a submitted Report/Submit Fix form's typed values across the
/// redirect-on-error round trip (e.g. a rejected attachment) so the vendor/employee doesn't have
/// to reselect the parts they'd already entered. The file input can't be repopulated (browsers
/// won't allow it) and the completion date doesn't survive the redirect either, so both still
/// need re-entering.</summary>
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
