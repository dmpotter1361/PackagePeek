using System.Text.RegularExpressions;

namespace AmazonTracker;

/// <summary>
/// Pure, UI-free decision logic for orders — classification, date parsing, overdue
/// grading. Extracted here so it can be unit-tested (see PackagePeek.Tests). All
/// time-dependent methods take an explicit "now" so tests are deterministic.
/// </summary>
public static class OrderLogic
{
    public enum Urgency { Normal, Late, VeryLate }

    /// <summary>Map a status line to a coarse delivery stage. Rejects long/code-like text.</summary>
    public static DeliveryStage Classify(string status)
    {
        var s = status.ToLowerInvariant();

        // Guard against page prose / script-comment text masquerading as a status.
        if (s.Length > 34 || s.IndexOfAny(new[] { ':', ';', '/' }) >= 0
            || s.Contains("node") || s.Contains("false-positive") || s.Contains(" not "))
            return DeliveryStage.Unknown;

        if (s.Contains("cancel")) return DeliveryStage.Canceled;
        if (s.Contains("return")) return DeliveryStage.Canceled; // a return heading back isn't incoming
        if (s.Contains("delivered")) return DeliveryStage.Delivered;
        if (s.Contains("out for delivery") || s.Contains("arriving today")) return DeliveryStage.OutForDelivery;
        // Processing must be checked before Shipped: "not yet shipped" contains "shipped".
        if (s.Contains("not yet shipped") || s.Contains("preparing") || s.Contains("ordered"))
            return DeliveryStage.Processing;
        if (s.Contains("shipped") || s.Contains("arriving") || s.Contains("expected") || s.Contains("on the way"))
            return DeliveryStage.Shipped;
        return DeliveryStage.Unknown;
    }

    /// <summary>
    /// Parse a delivery date from free text ("today", "tomorrow", "Jan 9",
    /// "January 9, 2026", "Mon, Jan 9"). Returns null when no date is found.
    /// </summary>
    public static DateTime? ParseWhen(string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.ToLowerInvariant();
        var today = now.Date;
        if (s.Contains("today")) return today;
        if (s.Contains("tomorrow")) return today.AddDays(1);
        if (s.Contains("yesterday")) return today.AddDays(-1);

        var m = Regex.Match(s, @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(\d{1,2})(?:,?\s*(\d{4}))?");
        if (!m.Success) return null;

        int month = "jan feb mar apr may jun jul aug sep oct nov dec".Split(' ').ToList().IndexOf(m.Groups[1].Value) + 1;
        if (month == 0) return null;
        int day = int.Parse(m.Groups[2].Value);
        int year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : today.Year;

        DateTime parsed;
        try { parsed = new DateTime(year, month, day); }
        catch { return null; }

        // Only roll back a year for an implausibly far-future date with no year given —
        // e.g. "Dec 28" seen in early January is really last year. A normal near-future
        // delivery like "June 29" two weeks out must stay in the current year.
        if (!m.Groups[3].Success && parsed > today.AddMonths(6)) parsed = parsed.AddYears(-1);
        return parsed;
    }

    /// <summary>Did this arrive today? Bare "Delivered" with no parseable date is treated as recent.</summary>
    public static bool DeliveredToday(string text, DateTime now)
    {
        var s = text.ToLowerInvariant();
        if (s.Contains("today")) return true;
        if (s.Contains("yesterday")) return false;
        var d = ParseWhen(text, now);
        return d is null || d.Value.Date == now.Date;
    }

    /// <summary>
    /// How overdue an order is, for color grading. Only items we KNOW are still in
    /// transit can be overdue; a future ETA is never overdue (that was the v0.1.0 bug).
    /// </summary>
    public static Urgency Overdue(DeliveryStage stage, DateTime? eta, DateTime now)
    {
        bool knownActive = stage is DeliveryStage.Processing or DeliveryStage.Shipped or DeliveryStage.OutForDelivery;
        if (!knownActive || eta is not DateTime e) return Urgency.Normal;

        var today = now.Date;
        if (e.Date >= today) return Urgency.Normal;             // today or future = not overdue
        return (today - e.Date).Days >= 3 ? Urgency.VeryLate : Urgency.Late;
    }
}
