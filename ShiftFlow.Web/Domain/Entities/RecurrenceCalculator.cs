namespace ShiftFlow.Domain.Entities;

/// <summary>Shared cadence date-math, lifted out of Contract so both the Preventive-Maintenance
/// generator and the Order-Type recurrence generator (RecurringOrder) compute occurrences the
/// same way. Cadence vocabulary is Contract.PmCadences ("Weekly"/"Monthly"/"Quarterly"/
/// "Semi-Annual"/"Annual").</summary>
public static class RecurrenceCalculator
{
    /// <summary>Every due date from startDate to endDate (inclusive) stepping by cadence. The first
    /// occurrence is startDate itself. Each occurrence is computed fresh from startDate (step *
    /// interval), never by chaining AddMonths off the previous occurrence — a start date of the
    /// 31st chained monthly would clamp to the 28th in February and then never recover the 31st in
    /// any later month, since AddMonths(1) from the already-clamped Feb 28 keeps landing on the
    /// 28th forever (confirmed live: Jan 31 -> Feb 28 -> Mar 28 -> ... instead of Mar 31). Computing
    /// from the original startDate each time means only the months genuinely too short (Feb) clamp,
    /// and every other month still lands on the intended day.</summary>
    public static List<DateTime> ComputeOccurrenceDueDates(DateTime startDate, DateTime endDate, string cadence)
    {
        var dates = new List<DateTime>();
        var start = startDate.Date;
        var end = endDate.Date;
        for (var step = 0; ; step++)
        {
            var occurrence = cadence switch
            {
                "Weekly" => start.AddDays(7 * step),
                "Monthly" => start.AddMonths(step),
                "Quarterly" => start.AddMonths(3 * step),
                "Semi-Annual" => start.AddMonths(6 * step),
                "Annual" => start.AddYears(step),
                _ => throw new InvalidOperationException($"Unknown cadence: {cadence}"),
            };
            if (occurrence > end) break;
            dates.Add(occurrence);
        }
        return dates;
    }
}
