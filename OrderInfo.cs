using System.Text.Json.Serialization;

namespace AmazonTracker;

/// <summary>
/// One tracked shipment as read off the Amazon orders page. A single order can
/// contain several shipments; we track at the shipment level because that's the
/// unit that has its own delivery status / ETA.
/// </summary>
public sealed class OrderInfo
{
    /// <summary>Amazon order id, e.g. "112-1234567-1234567".</summary>
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = "";

    /// <summary>A stable key for this shipment (order id + item signature) used for de-duping notifications.</summary>
    [JsonPropertyName("shipmentKey")]
    public string ShipmentKey { get; set; } = "";

    /// <summary>Human title — usually the first item's name, "(+2 more)" appended when multiple.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Raw status line Amazon shows, e.g. "Arriving today", "Out for delivery", "Delivered".</summary>
    [JsonPropertyName("statusText")]
    public string StatusText { get; set; } = "";

    /// <summary>ETA / delivery date text as shown, e.g. "Today by 10 PM", "Tomorrow", "Jan 9".</summary>
    [JsonPropertyName("etaText")]
    public string EtaText { get; set; } = "";

    /// <summary>Stops-away count when Amazon Logistics exposes it; null otherwise.</summary>
    [JsonPropertyName("stopsAway")]
    public int? StopsAway { get; set; }

    /// <summary>Delivery time window when shown, e.g. "12:15 PM - 3:15 PM" or "by 9 PM".</summary>
    [JsonPropertyName("deliveryWindow")]
    public string DeliveryWindow { get; set; } = "";

    /// <summary>Direct link to this order's detail/track page on Amazon.</summary>
    [JsonPropertyName("orderUrl")]
    public string OrderUrl { get; set; } = "";

    /// <summary>Product thumbnail URL (Amazon image CDN); empty when none was found.</summary>
    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = "";

    /// <summary>Carrier tracking number, e.g. "TBA331903997192" or a UPS/USPS/FedEx number.</summary>
    [JsonPropertyName("trackingId")]
    public string TrackingId { get; set; } = "";

    /// <summary>Carrier name as shown ("Amazon", "UPS", "USPS", …); may be empty.</summary>
    [JsonPropertyName("carrier")]
    public string Carrier { get; set; } = "";

    /// <summary>Coarse bucket derived from <see cref="StatusText"/>; set during normalization.</summary>
    [JsonIgnore]
    public DeliveryStage Stage { get; set; } = DeliveryStage.Unknown;

    /// <summary>For delivered items only: true when delivered today. Past deliveries are
    /// assumed already picked up and get dropped from the dashboard/notifications.</summary>
    [JsonIgnore]
    public bool DeliveredToday { get; set; }

    /// <summary>The ETA / delivery date parsed from <see cref="EtaText"/>, when we could read one.
    /// Used to color-grade overdue items.</summary>
    [JsonIgnore]
    public DateTime? EtaDate { get; set; }
}

public enum DeliveryStage
{
    Unknown = 0,
    Processing,      // ordered, not yet shipped
    Shipped,         // in transit
    OutForDelivery,  // on the van / arriving today
    Delivered,
    Canceled         // canceled/refunded — will never arrive
}
