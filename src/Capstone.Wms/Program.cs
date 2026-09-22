using Capstone.Core;
var builder = WebApplication.CreateBuilder(args);
ServiceSetup.Configure(builder);
var app = builder.Build();
ServiceSetup.Secure(app);
app.MapPost("/api/acceptances", async (OrderRequest order, HttpContext context, SqlStore store, CancellationToken ct) =>
{
    var error = OrderRules.Validate(order);
    if (error is not null) return Results.BadRequest(new { error });
    var outcome = await store.Accept(ServiceSetup.Source(context), order, ct);
    return Results.Json(outcome.Receipt, statusCode: outcome.Duplicate ? 200 : 201);
});
app.Run();
