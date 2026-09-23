using Capstone.Core;
namespace Capstone.UnitTests;
public class AutomationPlanTests
{
    private static MessageTemplate[] Templates()=>Enumerable.Range(1,1000).Select(i=>new MessageTemplate(i,$"SKU-{i}",i)).ToArray();
    [Fact] public void Thousand_message_batch_has_exactly_thirty_percent_injected_errors()
    {
        var plan=AutomationPlan.Create(Templates(),new());
        Assert.Equal(1000,plan.Count);Assert.Equal(300,plan.Count(m=>m.Scenario!="Valid"));
        Assert.Equal(1000,plan.Select(m=>m.TemplateId).Distinct().Count());
        var baseline=new Dictionary<Guid,OrderRequest>();
        foreach(var m in plan)
        {
            if(m.Scenario=="Valid"){Assert.Null(OrderRules.Validate(m.Order));baseline.Add(m.Order.OrderId,m.Order);}
            else if(m.Scenario=="InvalidQuantity")Assert.Equal("quantity_out_of_range",OrderRules.Validate(m.Order));
            else {Assert.True(baseline.ContainsKey(m.Order.OrderId));Assert.False(OrderRules.SameContent(baseline[m.Order.OrderId],m.Order));Assert.Null(OrderRules.Validate(m.Order));}
        }
    }
    [Fact] public void Seed_repeats_scenarios_but_not_business_identities()
    {
        var a=AutomationPlan.Create(Templates(),new());var b=AutomationPlan.Create(Templates(),new());
        Assert.Equal(a.Select(m=>(m.TemplateId,m.Scenario,m.Order.Sku,m.Order.Quantity)),b.Select(m=>(m.TemplateId,m.Scenario,m.Order.Sku,m.Order.Quantity)));
        Assert.NotEqual(a[0].Order.OrderId,b[0].Order.OrderId);
    }
    [Theory][InlineData(0)][InlineData(80)] public void Error_boundaries_preserve_a_valid_conflict_baseline(int percent)
    {var p=AutomationPlan.Create(Templates(),new(10,percent));Assert.Equal(percent/10,p.Count(m=>m.Scenario!="Valid"));Assert.Equal("Valid",p[0].Scenario);}
}
