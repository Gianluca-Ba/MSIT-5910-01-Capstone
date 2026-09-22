using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Capstone.Core;

public static class ServiceSetup
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Configuration.AddXmlFile("settings.xml", optional: false).AddEnvironmentVariables();
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4096);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddSingleton(new SqlStore(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Configure ConnectionStrings__Database outside Git.")));
    }
    public static void Secure(WebApplication app)
    {
        var clients = app.Configuration.GetSection("Auth:Clients").GetChildren().ToDictionary(x => x.Key, x => x.Value ?? "");
        if (clients.Count == 0 || clients.Any(x => x.Key.Length > 64 || x.Value.Length < 32) || clients.Values.Distinct(StringComparer.Ordinal).Count() != clients.Count)
            throw new InvalidOperationException("Configure unique API keys of at least 32 characters for Auth__Clients__source.");
        app.Use(async (context, next) =>
        {
            // Unit 4 HTTP services accept loopback only. Network deployment requires HTTPS.
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) { context.Response.StatusCode = 403; return; }
            if (context.Request.Path == "/health") { await next(); return; }
            var supplied = context.Request.Headers["X-Api-Key"].ToString();
            var match = clients.FirstOrDefault(x => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(x.Value)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))));
            if (match.Key is null) { context.Response.StatusCode = 401; return; }
            context.Items["SourceId"] = match.Key;
            try { await next(); }
            catch (OrderConflictException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "immutable_order_conflict" }); }
            catch (Microsoft.Data.SqlClient.SqlException) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "database_unavailable" }); }
        });
        app.MapGet("/health", async (SqlStore store, CancellationToken ct) =>
        {
            try { return await store.Healthy(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
            catch (Microsoft.Data.SqlClient.SqlException) { return Results.StatusCode(503); }
        });
    }
    public static string Source(HttpContext context) => (string)context.Items["SourceId"]!;
}
