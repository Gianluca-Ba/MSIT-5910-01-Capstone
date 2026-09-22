using System.Net.Http.Json;
using System.Text.Json;
using Capstone.Core;
namespace Capstone.Erp;

public sealed class OutboxWorker(SqlStore store, IHttpClientFactory clients, IConfiguration configuration, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Delivery:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = await store.Next(stoppingToken);
                if (next is not null)
                {
                    var (source, order) = next.Value;
                    var status = "RecoveryRequired";
                    var category = "transport_uncertain";
                    Receipt? receipt = null;
                    try
                    {
                        var key = configuration[$"Delivery:Keys:{source}"];
                        if (string.IsNullOrWhiteSpace(key)) category = "missing_delivery_identity";
                        else
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Post, "api/acceptances") { Content = JsonContent.Create(order) };
                            request.Headers.Add("X-Api-Key", key);
                            using var response = await clients.CreateClient("wms").SendAsync(request, stoppingToken);
                            if ((int)response.StatusCode is 200 or 201)
                            {
                                receipt = await response.Content.ReadFromJsonAsync<Receipt>(stoppingToken);
                                if (DeliveryOutcome.ValidReceipt(receipt, source, order.OrderId)) { status = "Acknowledged"; category = "accepted"; }
                                else { receipt = null; category = "invalid_receipt"; }
                            }
                            else { status = DeliveryOutcome.ForHttpFailure((int)response.StatusCode); category = $"http_{(int)response.StatusCode}"; }
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (JsonException) { category = "invalid_receipt"; }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                    // A crash before this commit leaves Pending and permits safe redelivery on restart.
                    await store.Complete(source, order.OrderId, status, category, receipt, stoppingToken);
                    logger.LogInformation("Order {OrderId} delivery state {Status}", order.OrderId, status);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Microsoft.Data.SqlClient.SqlException) { logger.LogWarning("Outbox database unavailable; pending records remain durable."); }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
