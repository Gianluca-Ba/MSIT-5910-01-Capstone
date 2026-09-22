using Capstone.Core;
using Capstone.Erp;
var builder = WebApplication.CreateBuilder(args);
ServiceSetup.Configure(builder);
builder.Services.AddHttpClient("wms", client =>
{
    var target = new Uri(builder.Configuration["Delivery:WmsUrl"]!);
    if (!target.IsLoopback) throw new InvalidOperationException("Unit 4 WMS URL must use loopback.");
    client.BaseAddress = target;
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHostedService<OutboxWorker>();
var app = builder.Build();
ServiceSetup.Secure(app);
app.MapPost("/api/orders", async (OrderRequest order, HttpContext context, SqlStore store, CancellationToken ct) =>
{
    var error = OrderRules.Validate(order);
    if (error is not null) return Results.BadRequest(new { error });
    var status = await store.Submit(ServiceSetup.Source(context), order, ct);
    return Results.Accepted($"/api/orders/{order.OrderId}", status);
});
app.MapGet("/api/orders/{id:guid}", async (Guid id, HttpContext context, SqlStore store, CancellationToken ct) =>
{
    var status = await store.Get(ServiceSetup.Source(context), id, ct);
    return status is null ? Results.NotFound() : Results.Ok(status);
});
app.Run();
