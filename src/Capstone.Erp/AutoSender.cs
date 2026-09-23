using System.Text;
using Microsoft.Data.SqlClient;
namespace Capstone.Erp;

public sealed class AutoSender(AutomationStore store,IHttpClientFactory clients,IConfiguration config,ILogger<AutoSender> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var recovered=false;
        while(!ct.IsCancellationRequested)
        {
            try
            {
                if(!recovered){await store.Recover(ct);recovered=true;}
                await store.Reconcile(ct);
                var item=await store.Claim(ct);
                if(item is { } m)
                {
                    var code=0;var body="No HTTP response; delivery outcome unknown.";
                    try
                    {
                        using var request=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=new StringContent(m.Payload,Encoding.UTF8,"application/json")};
                        request.Headers.Add("X-Api-Key",config[$"Auth:Clients:{m.Source}"]);
                        using var response=await clients.CreateClient("auto-erp").SendAsync(request,ct);
                        code=(int)response.StatusCode;body=await response.Content.ReadAsStringAsync(ct);
                    }
                    catch(HttpRequestException){}
                    catch(OperationCanceledException) when(!ct.IsCancellationRequested){}
                    await store.Record(m.Run,m.Sequence,code,body,ct);
                    await Task.Delay(m.Interval,ct);
                }
                else await Task.Delay(500,ct);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(SqlException){recovered=false;logger.LogWarning("Automatic sender database unavailable; active run will be interrupted before further sending.");await Task.Delay(3000,ct);}
        }
    }
}
