using OfficeOpenXml;

namespace ShiftFlow.Web.Services;

/// <summary>EPPlus' license context is process-wide static state, so it only needs setting once.
/// Callers used to assign it on every export request.</summary>
public static class ExcelHelper
{
    private static int _set;

    public static void EnsureLicense()
    {
        if (Interlocked.Exchange(ref _set, 1) == 0)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }
}
