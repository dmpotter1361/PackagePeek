using AmazonTracker;
using Xunit;

namespace PackagePeek.Tests;

/// <summary>
/// Regression tests for the order decision logic. Each bug we hit in real-world
/// testing gets a test here so it can't silently come back.
/// </summary>
public class OrderLogicTests
{
    // A fixed "now" so date math is deterministic.
    private static readonly DateTime Now = new(2026, 6, 14);

    // --- Classify -------------------------------------------------------------

    [Theory]
    [InlineData("Arriving today", DeliveryStage.OutForDelivery)]
    [InlineData("Out for delivery", DeliveryStage.OutForDelivery)]
    [InlineData("Arriving tomorrow", DeliveryStage.Shipped)]
    [InlineData("Now arriving June 22 - June 24", DeliveryStage.Shipped)] // future estimate = in transit, not OFD
    [InlineData("Delivered June 12", DeliveryStage.Delivered)]
    [InlineData("Cancelled", DeliveryStage.Canceled)]
    [InlineData("Order canceled", DeliveryStage.Canceled)]
    [InlineData("Return received", DeliveryStage.Canceled)] // a return isn't an incoming package
    [InlineData("Not yet shipped", DeliveryStage.Processing)]
    [InlineData("", DeliveryStage.Unknown)]
    public void Classify_MapsStatusToStage(string status, DeliveryStage expected)
        => Assert.Equal(expected, OrderLogic.Classify(status));

    [Fact]
    public void Classify_RejectsScriptCommentText()
    {
        // The Amazon inline-script comment that fooled us early on must never classify as Cancelled.
        const string junk = "Cancelled order: check the shipment status text node only, NOT the whole // card";
        Assert.Equal(DeliveryStage.Unknown, OrderLogic.Classify(junk));
    }

    // --- ParseWhen ------------------------------------------------------------

    [Fact]
    public void ParseWhen_FutureDate_StaysCurrentYear()
    {
        // Regression for the v0.1.0 bug: "June 29" two weeks out was rolled back to last year.
        Assert.Equal(new DateTime(2026, 6, 29), OrderLogic.ParseWhen("June 29", Now));
    }

    [Fact]
    public void ParseWhen_DecemberSeenInJanuary_RollsBackAYear()
    {
        var jan5 = new DateTime(2026, 1, 5);
        Assert.Equal(new DateTime(2025, 12, 28), OrderLogic.ParseWhen("Delivered Dec 28", jan5));
    }

    [Theory]
    [InlineData("today", 2026, 6, 14)]
    [InlineData("tomorrow", 2026, 6, 15)]
    [InlineData("Delivered June 12", 2026, 6, 12)]
    [InlineData("January 9, 2027", 2027, 1, 9)]
    public void ParseWhen_ParsesCommonForms(string text, int y, int m, int d)
        => Assert.Equal(new DateTime(y, m, d), OrderLogic.ParseWhen(text, Now));

    [Fact]
    public void ParseWhen_NoDate_ReturnsNull()
        => Assert.Null(OrderLogic.ParseWhen("Shipped", Now));

    // --- DeliveredToday -------------------------------------------------------

    [Theory]
    [InlineData("Delivered today", true)]
    [InlineData("Delivered June 14", true)]
    [InlineData("Delivered June 12", false)]
    [InlineData("Delivered yesterday", false)]
    public void DeliveredToday_OnlyTrueForToday(string text, bool expected)
        => Assert.Equal(expected, OrderLogic.DeliveredToday(text, Now));

    // --- Overdue (color grading) ---------------------------------------------

    [Fact]
    public void Overdue_FutureDate_IsNormal()
        => Assert.Equal(OrderLogic.Urgency.Normal, OrderLogic.Overdue(DeliveryStage.Shipped, new DateTime(2026, 6, 29), Now));

    [Fact]
    public void Overdue_OneDayLate_IsLate()
        => Assert.Equal(OrderLogic.Urgency.Late, OrderLogic.Overdue(DeliveryStage.Shipped, new DateTime(2026, 6, 13), Now));

    [Fact]
    public void Overdue_ThreeDaysLate_IsVeryLate()
        => Assert.Equal(OrderLogic.Urgency.VeryLate, OrderLogic.Overdue(DeliveryStage.Shipped, new DateTime(2026, 6, 11), Now));

    [Fact]
    public void Overdue_Delivered_IsNeverOverdue()
        => Assert.Equal(OrderLogic.Urgency.Normal, OrderLogic.Overdue(DeliveryStage.Delivered, new DateTime(2026, 6, 1), Now));

    [Fact]
    public void Overdue_UnknownStatus_IsNeverOverdue()
        => Assert.Equal(OrderLogic.Urgency.Normal, OrderLogic.Overdue(DeliveryStage.Unknown, new DateTime(2026, 6, 1), Now));
}
