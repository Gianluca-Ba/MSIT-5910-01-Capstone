using Capstone.Core;
var builder = WebApplication.CreateBuilder(args);
ServiceSetup.Configure(builder);
var app = builder.Build();
ServiceSetup.Secure(app);
app.MapGet("/api/acceptances/{id:guid}", async (Guid id, HttpContext context, SqlStore store, CancellationToken ct) =>
{
    var evidence = await store.GetAcceptance(ServiceSetup.Source(context), id, ct);
    return evidence is null ? Results.NotFound() : Results.Ok(evidence);
});
app.MapPost("/api/acceptances", async (OrderRequest order, HttpContext context, SqlStore store, CancellationToken ct) =>
{
    var error = OrderRules.Validate(order);
    if (error is not null) return Results.BadRequest(new { error });
    var outcome = await store.Accept(ServiceSetup.Source(context), order, ct);
    return Results.Json(outcome.Receipt, statusCode: outcome.Duplicate ? 200 : 201);
});
app.Run();
