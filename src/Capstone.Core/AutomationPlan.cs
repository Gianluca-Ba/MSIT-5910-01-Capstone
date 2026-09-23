namespace Capstone.Core;

public sealed record MessageTemplate(int TemplateId, string Sku, int Quantity);
public sealed record RunOptions(int Count = 1000, int ErrorPercent = 30, int IntervalMs = 500, int Seed = 5910);
public sealed record PlannedMessage(int Sequence, int TemplateId, string Scenario, string Expected, OrderRequest Order);

public static class AutomationPlan
{
    public static bool Valid(RunOptions o) => o.Count is >= 10 and <= 1000 && o.ErrorPercent is >= 0 and <= 80 && o.IntervalMs is >= 100 and <= 5000;
    public static List<PlannedMessage> Create(IReadOnlyList<MessageTemplate> templates, RunOptions options)
    {
        if (!Valid(options) || templates.Count < options.Count) throw new ArgumentException("Invalid run options or insufficient templates.");
        var random = new Random(options.Seed);
        var pool = templates.ToArray(); random.Shuffle(pool);
        var slots = Enumerable.Range(1, options.Count - 1).ToArray(); random.Shuffle(slots);
        var errors = slots.Take((int)Math.Round(options.Count * options.ErrorPercent / 100d, MidpointRounding.AwayFromZero)).ToHashSet();
        var results = new List<PlannedMessage>();
        OrderRequest? previous = null;
        for (var i = 0; i < options.Count; i++)
        {
            var template = pool[i];
            // The seed reproduces payload fields and scenario order; identities are new for every run.
            var order = new OrderRequest(1, Guid.NewGuid(), template.Sku, pool[random.Next(pool.Length)].Quantity, Guid.NewGuid());
            var scenario = "Valid"; var expected = "Acknowledged";
            if (errors.Contains(i))
            {
                if (i % 2 == 0 && previous is not null) { scenario = "Conflict"; expected = "ConflictRejected"; order = previous with { Quantity = previous.Quantity + 1, CorrelationId = Guid.NewGuid() }; }
                else { scenario = "InvalidQuantity"; expected = "ValidationRejected"; order = order with { Quantity = 0 }; }
            }
            else previous = order;
            results.Add(new(i + 1, template.TemplateId, scenario, expected, order));
        }
        return results;
    }
}
