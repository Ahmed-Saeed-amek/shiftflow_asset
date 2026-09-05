namespace ShiftFlow.Domain.Entities;

/// <summary>Shared cadence date-math, lifted out of Contract so both the Preventive-Maintenance
/// generator and the Order-Type recurrence generator (RecurringOrder) compute occurrences the
/// same way. Cadence vocabulary is Contract.PmCadences ("Weekly"/"Monthly"/"Quarterly"/
/// "Semi-Annual"/"Annual").</summary>
public static class RecurrenceCalculator
{
    /// <summary>Every due date from startDate to endDate (inclusive) stepping by cadence. The first
    /// occurrence is startDate itself.</summary>
    public static List<DateTime> ComputeOccurrenceDueDates(DateTime startDate, DateTime endDate, string cadence)
    {
        var dates = new List<DateTime>();
        var current = startDate.Date;
        var end = endDate.Date;
        while (current <= end)
        {
            dates.Add(current);
            current = cadence switch
            {
                "Weekly" => current.AddDays(7),
                "Monthly" => current.AddMonths(1),
                "Quarterly" => current.AddMonths(3),
                "Semi-Annual" => current.AddMonths(6),
                "Annual" => current.AddYears(1),
                _ => throw new InvalidOperationException($"Unknown cadence: {cadence}"),
            };
        }
        return dates;
    }
}
