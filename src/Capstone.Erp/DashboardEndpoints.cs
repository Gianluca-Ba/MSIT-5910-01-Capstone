using System.Net.Http.Json;
using Capstone.Core;

namespace Capstone.Erp;

public static class DashboardEndpoints
{
    public static void Map(WebApplication app)
    {
        // The authenticated ERP source selects its WMS identity. No browser-supplied target or key.
        app.MapGet("/api/verification/{id:guid}", (Guid id, HttpContext context, IHttpClientFactory clients, IConfiguration config, CancellationToken ct) =>
            Forward(HttpMethod.Get, $"api/acceptances/{id}", null, context, clients, config, ct));
        app.MapPost("/api/verification/acceptances", (OrderRequest order, HttpContext context, IHttpClientFactory clients, IConfiguration config, CancellationToken ct) =>
            Forward(HttpMethod.Post, "api/acceptances", order, context, clients, config, ct));
    }

    private static async Task<IResult> Forward(HttpMethod method, string path, OrderRequest? order,
        HttpContext context, IHttpClientFactory clients, IConfiguration config, CancellationToken ct)
    {
        var key = config[$"Delivery:Keys:{ServiceSetup.Source(context)}"];
        if (string.IsNullOrWhiteSpace(key)) return Results.Json(new { error = "missing_delivery_identity" }, statusCode: 503);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", key);
        if (order is not null) request.Content = JsonContent.Create(order);
        try
        {
            using var response = await clients.CreateClient("wms").SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Results.Json(new { error = "wms_unreachable_outcome_unknown" }, statusCode: 502); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Results.Json(new { error = "wms_timeout_outcome_unknown" }, statusCode: 504); }
    }
}
