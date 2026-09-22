using System.Text.RegularExpressions;
namespace Capstone.Core;

public sealed record OrderRequest(int Version, Guid OrderId, string? Sku, int Quantity, Guid CorrelationId);
public sealed record Receipt(string SourceId, Guid OrderId, Guid ReceiptId, DateTimeOffset AcceptedAt);
public sealed record OrderStatus(string SourceId, OrderRequest Order, string Status, Receipt? Receipt);
public sealed record Acceptance(Receipt Receipt, bool Duplicate);
public sealed class OrderConflictException : Exception;
public static partial class OrderRules
{
    public static string? Validate(OrderRequest order)
    {
        if (order.Version != 1) return "unsupported_version";
        if (order.OrderId == Guid.Empty) return "order_id_required";
        if (order.CorrelationId == Guid.Empty) return "correlation_id_required";
        if (order.Sku is null || !SkuPattern().IsMatch(order.Sku)) return "invalid_sku";
        if (order.Quantity is < 1 or > 100000) return "quantity_out_of_range";
        return null;
    }
    // Correlation is tracing metadata, not immutable business content.
    public static bool SameContent(OrderRequest a, OrderRequest b) => a.Version == b.Version && a.OrderId == b.OrderId && string.Equals(a.Sku, b.Sku, StringComparison.Ordinal) && a.Quantity == b.Quantity;
    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
public static class DeliveryOutcome
{
    public static bool ValidReceipt(Receipt? r, string source, Guid id) => r is not null && r.SourceId == source && r.OrderId == id && r.ReceiptId != Guid.Empty && r.AcceptedAt != default;
    public static string ForHttpFailure(int status) => status is 400 or 409 or 422 ? "Rejected" : "RecoveryRequired";
}
