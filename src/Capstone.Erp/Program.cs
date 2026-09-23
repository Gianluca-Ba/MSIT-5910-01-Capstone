using Capstone.Core;
using Capstone.Erp;
var builder = WebApplication.CreateBuilder(args);
ServiceSetup.Configure(builder);
builder.Services.AddSingleton<CustomizationStore>();
builder.Services.AddHttpClient("wms", client =>
{
    var target = new Uri(builder.Configuration["Delivery:WmsUrl"]!);
    if (!target.IsLoopback) throw new InvalidOperationException("Unit 4 WMS URL must use loopback.");
    client.BaseAddress = target;
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddSingleton<AutomationStore>();
builder.Services.AddHttpClient("auto-erp", client => { var url = new Uri(builder.Configuration["Automation:ErpUrl"] ?? "http://127.0.0.1:5080/"); if (!url.IsLoopback) throw new InvalidOperationException("Auto sender requires a loopback ERP URL."); client.BaseAddress=url;client.Timeout=TimeSpan.FromSeconds(10); });
builder.Services.AddHostedService<AutoSender>();
var app = builder.Build();
ServiceSetup.Secure(app, dashboard: true);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/message-types") &&
        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        limit.MaxRequestBodySize = 65536;
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
DashboardEndpoints.Map(app);
CustomizationEndpoints.Map(app);
app.MapPost("/api/automation/arm", async (RunOptions options,HttpContext context,AutomationStore store,CancellationToken ct) =>
    !AutomationPlan.Valid(options) ? Results.BadRequest(new {error="Use 10–1000 messages, 0–80% errors and a 100–5000 ms interval."}) : Results.Ok(new {runId=await store.Arm(ServiceSetup.Source(context),options,ct)}));
app.MapGet("/api/automation",async(HttpContext context,AutomationStore store,CancellationToken ct)=>Results.Content(await store.Read(ServiceSetup.Source(context),null,null,ct) ?? "[]","application/json"));
app.MapGet("/api/automation/{id:guid}",async(Guid id,HttpContext context,AutomationStore store,CancellationToken ct)=> {var json=await store.Read(ServiceSetup.Source(context),id,null,ct);return json is null ? Results.NotFound() : Results.Content(json,"application/json");});
app.MapGet("/api/automation/{id:guid}/messages/{sequence:int}",async(Guid id,int sequence,HttpContext context,AutomationStore store,CancellationToken ct)=> {var json=await store.Read(ServiceSetup.Source(context),id,sequence,ct);return json is null ? Results.NotFound() : Results.Content(json,"application/json");});
app.MapPost("/api/automation/{id:guid}/run",async(Guid id,HttpContext context,AutomationStore store,CancellationToken ct)=>await store.Control(ServiceSetup.Source(context),id,true,ct)? Results.Ok():Results.Conflict(new {error="Run is not armed, or another batch is active."}));
app.MapPost("/api/automation/{id:guid}/stop",async(Guid id,HttpContext context,AutomationStore store,CancellationToken ct)=>await store.Control(ServiceSetup.Source(context),id,false,ct)? Results.Ok():Results.Conflict(new {error="Run is no longer active."}));
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
