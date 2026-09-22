using Capstone.Core;
namespace Capstone.UnitTests;

public class OrderRulesTests
{
    private static OrderRequest Valid() => new(1, Guid.NewGuid(), "SKU-01", 5, Guid.NewGuid());
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void Invalid_quantity_is_rejected(int quantity) => Assert.Equal("quantity_out_of_range", OrderRules.Validate(Valid() with { Quantity = quantity }));
    [Theory]
    [InlineData(1)]
    [InlineData(100000)]
    public void Quantity_boundaries_are_accepted(int quantity) => Assert.Null(OrderRules.Validate(Valid() with { Quantity = quantity }));
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SKU\n")]
    [InlineData(" SKU")]
    [InlineData("x';DROP TABLE Orders;--")]
    public void Invalid_sku_is_rejected(string? sku) => Assert.Equal("invalid_sku", OrderRules.Validate(Valid() with { Sku = sku }));
    [Fact] public void Sku_length_is_bounded() => Assert.Equal("invalid_sku", OrderRules.Validate(Valid() with { Sku = new string('A', 65) }));
    [Fact] public void Unsupported_version_is_rejected() => Assert.Equal("unsupported_version", OrderRules.Validate(Valid() with { Version = 2 }));
    [Fact] public void Missing_order_id_is_rejected() => Assert.Equal("order_id_required", OrderRules.Validate(Valid() with { OrderId = Guid.Empty }));
    [Fact] public void Missing_correlation_is_rejected() => Assert.Equal("correlation_id_required", OrderRules.Validate(Valid() with { CorrelationId = Guid.Empty }));
    [Fact] public void Correlation_change_does_not_change_business_content() { var o = Valid(); Assert.True(OrderRules.SameContent(o, o with { CorrelationId = Guid.NewGuid() })); }
    [Fact] public void Quantity_change_is_a_conflict() { var o = Valid(); Assert.False(OrderRules.SameContent(o, o with { Quantity = 6 })); }
    [Fact] public void Sku_comparison_is_case_sensitive() { var o = Valid(); Assert.False(OrderRules.SameContent(o, o with { Sku = "sku-01" })); }
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Infrastructure_failure_requires_recovery(int status) => Assert.Equal("RecoveryRequired", DeliveryOutcome.ForHttpFailure(status));
    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    [InlineData(422)]
    public void Explicit_business_failure_is_rejected(int status) => Assert.Equal("Rejected", DeliveryOutcome.ForHttpFailure(status));
    [Fact] public void Wrong_order_receipt_is_not_acknowledged() => Assert.False(DeliveryOutcome.ValidReceipt(new("demo", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow), "demo", Guid.NewGuid()));
    [Fact] public void Wrong_source_receipt_is_not_acknowledged() { var id = Guid.NewGuid(); Assert.False(DeliveryOutcome.ValidReceipt(new("other", id, Guid.NewGuid(), DateTimeOffset.UtcNow), "demo", id)); }
    [Fact] public void Valid_receipt_is_acknowledged() { var id = Guid.NewGuid(); Assert.True(DeliveryOutcome.ValidReceipt(new("demo", id, Guid.NewGuid(), DateTimeOffset.UtcNow), "demo", id)); }
}
