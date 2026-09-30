using Capstone.Core;

namespace Capstone.UnitTests;

public class Unit5RegressionTests
{
    [Fact]
    public void Conflict_injection_at_maximum_quantity_remains_valid_business_input()
    {
        var templates = Enumerable.Range(1, 1000)
            .Select(i => new MessageTemplate(i, $"SKU-{i}", 100000)).ToArray();
        var plan = AutomationPlan.Create(templates, new());
        var conflicts = plan.Where(m => m.Scenario == "Conflict").ToArray();
        Assert.NotEmpty(conflicts);
        Assert.All(conflicts, m => Assert.Null(OrderRules.Validate(m.Order)));
        Assert.All(conflicts, m => Assert.Equal("ConflictRejected", m.Expected));
    }

    [Fact]
    public void Missing_receipt_or_incomplete_receipt_cannot_acknowledge_delivery()
    {
        var id = Guid.NewGuid();
        Assert.False(DeliveryOutcome.ValidReceipt(null, "demo", id));
        Assert.False(DeliveryOutcome.ValidReceipt(new("demo", id, Guid.Empty, DateTimeOffset.UtcNow), "demo", id));
        Assert.False(DeliveryOutcome.ValidReceipt(new("demo", id, Guid.NewGuid(), default), "demo", id));
    }

    [Fact]
    public void Identity_and_version_changes_are_business_conflicts()
    {
        var order = new OrderRequest(1, Guid.NewGuid(), "SKU-1", 1, Guid.NewGuid());
        Assert.False(OrderRules.SameContent(order, order with { OrderId = Guid.NewGuid() }));
        Assert.False(OrderRules.SameContent(order, order with { Version = 2 }));
    }

    [Theory]
    [InlineData(9, 30, 100)]
    [InlineData(1001, 30, 100)]
    [InlineData(10, -1, 100)]
    [InlineData(10, 81, 100)]
    [InlineData(10, 30, 99)]
    [InlineData(10, 30, 5001)]
    public void Invalid_run_settings_fail_before_generation(int count, int errorPercent, int interval)
    {
        Assert.Throws<ArgumentException>(() => AutomationPlan.Create([], new(count, errorPercent, interval)));
    }

    [Fact]
    public void Insufficient_templates_fail_before_generation() =>
        Assert.Throws<ArgumentException>(() => AutomationPlan.Create([], new()));
}
