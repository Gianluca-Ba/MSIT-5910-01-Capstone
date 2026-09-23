using System.Data;
using System.Text.Json;
using Capstone.Core;
using Microsoft.Data.SqlClient;
namespace Capstone.Erp;

public sealed class AutomationStore(IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private SqlConnection Connection() => new(config.GetConnectionString("Database"));
    private static void Parameters(SqlCommand cmd, string source, Guid id) { cmd.Parameters.AddWithValue("@source", source); cmd.Parameters.AddWithValue("@id", id); }
    public async Task<Guid> Arm(string source, RunOptions options, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        var templates = new List<MessageTemplate>();
        await using (var cmd = new SqlCommand("SELECT TemplateId,Sku,Quantity FROM dbo.MessageTemplate ORDER BY TemplateId", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct)) { while (await r.ReadAsync(ct)) templates.Add(new(r.GetInt32(0), r.GetString(1), r.GetInt32(2))); }
        var plan = AutomationPlan.Create(templates, options); var id = Guid.NewGuid();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        await using var create = new SqlCommand("""
            INSERT dbo.AutomationRun(RunId,SourceId,Seed,ErrorPercent,IntervalMs,MessageCount,State)
            VALUES(@id,@source,@seed,@error,@interval,@count,'Armed');
            """, c, tx);
        Parameters(create, source, id); create.Parameters.AddWithValue("@seed", options.Seed); create.Parameters.AddWithValue("@error", options.ErrorPercent);
        create.Parameters.AddWithValue("@interval", options.IntervalMs); create.Parameters.AddWithValue("@count", options.Count); await create.ExecuteNonQueryAsync(ct);
        foreach (var m in plan)
        {
            await using var insert = new SqlCommand("INSERT dbo.AutomationMessage(RunId,Sequence,TemplateId,OrderId,Scenario,Expected,Payload) VALUES(@id,@seq,@template,@order,@scenario,@expected,@payload)", c, tx);
            insert.Parameters.AddWithValue("@id", id); insert.Parameters.AddWithValue("@seq", m.Sequence); insert.Parameters.AddWithValue("@template", m.TemplateId);
            insert.Parameters.AddWithValue("@order", m.Order.OrderId); insert.Parameters.AddWithValue("@scenario", m.Scenario); insert.Parameters.AddWithValue("@expected", m.Expected);
            insert.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(m.Order, Json)); await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return id;
    }
    public async Task<bool> Control(string source, Guid id, bool start, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        // Serialize start decisions globally: one active batch protects the single outbox worker.
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var cmd = new SqlCommand(start ? """
            IF NOT EXISTS(SELECT 1 FROM dbo.AutomationRun WITH(UPDLOCK,HOLDLOCK) WHERE State IN ('Running','Draining','Stopping'))
            UPDATE dbo.AutomationRun SET State='Running',StartedAt=SYSUTCDATETIME() WHERE RunId=@id AND SourceId=@source AND State='Armed';
            """ : """
            UPDATE dbo.AutomationRun SET State='Stopping' WHERE RunId=@id AND SourceId=@source AND State IN ('Armed','Running','Draining');
            """, c, tx);
        Parameters(cmd, source, id); var changed = await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return changed > 0;
    }
    public async Task<string?> Read(string source, Guid? id, int? sequence, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        var sql = id is null ? "SELECT TOP(20) RunId runId,State state,CreatedAt createdAt,MessageCount messageCount,ErrorPercent errorPercent FROM dbo.AutomationRun WHERE SourceId=@source ORDER BY CreatedAt DESC FOR JSON PATH" : sequence is not null ? """
            SELECT m.Sequence sequence,m.Scenario scenario,m.Expected expected,JSON_QUERY(m.Payload) payload,m.State state,m.SentAt sentAt,m.AnalyzedAt analyzedAt,m.CompletedAt completedAt,m.HttpStatus httpStatus,m.Response response,JSON_QUERY(m.Receipt) receipt,m.Matched matched
            FROM dbo.AutomationMessage m JOIN dbo.AutomationRun r ON r.RunId=m.RunId WHERE r.SourceId=@source AND r.RunId=@id AND m.Sequence=@seq FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
            """ : """
            SELECT r.RunId runId,r.State state,r.Seed seed,r.ErrorPercent errorPercent,r.IntervalMs intervalMs,r.MessageCount messageCount,r.CreatedAt createdAt,r.StartedAt startedAt,r.FinishedAt finishedAt,
            JSON_QUERY((SELECT m.Sequence sequence,m.Scenario scenario,m.Expected expected,m.State state,m.Matched matched,m.SentAt sentAt,m.AnalyzedAt analyzedAt,m.CompletedAt completedAt,m.HttpStatus httpStatus FROM dbo.AutomationMessage m WHERE m.RunId=r.RunId ORDER BY m.Sequence FOR JSON PATH,INCLUDE_NULL_VALUES)) messages
            FROM dbo.AutomationRun r WHERE r.RunId=@id AND r.SourceId=@source FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
            """;
        await using var cmd = new SqlCommand(sql, c); Parameters(cmd, source, id ?? Guid.Empty); cmd.Parameters.AddWithValue("@seq", sequence ?? 0);
        // FOR JSON may split large results into multiple rows.
        var text = new System.Text.StringBuilder(); await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) text.Append(reader.GetString(0));
        return text.Length == 0 ? null : text.ToString();
    }
    public async Task Recover(CancellationToken ct) => await Execute("""
        UPDATE dbo.AutomationMessage SET State='Unknown',CompletedAt=SYSUTCDATETIME() WHERE State='Sending';
        UPDATE dbo.AutomationRun SET State='Interrupted' WHERE State IN ('Running','Draining');
        """, ct);
    private async Task Execute(string sql, CancellationToken ct) { await using var c = Connection(); await c.OpenAsync(ct); await using var cmd = new SqlCommand(sql,c); await cmd.ExecuteNonQueryAsync(ct); }
    public async Task Reconcile(CancellationToken ct) => await Execute("""
        UPDATE m SET State=o.Status,Receipt=o.Receipt,CompletedAt=SYSUTCDATETIME(),Matched=CASE WHEN m.Expected=o.Status THEN 1 ELSE 0 END
        FROM dbo.AutomationMessage m JOIN dbo.AutomationRun r ON r.RunId=m.RunId
        JOIN dbo.OutboxMessage o ON o.SourceId=r.SourceId AND o.OrderId=m.OrderId
        WHERE (m.State='AwaitingDelivery' OR (m.State='Unknown' AND m.Expected='Acknowledged')) AND o.Status<>'Pending';
        UPDATE m SET State='Cancelled',CompletedAt=SYSUTCDATETIME() FROM dbo.AutomationMessage m JOIN dbo.AutomationRun r ON r.RunId=m.RunId WHERE r.State IN ('Stopping','Interrupted') AND m.State='Queued';
        UPDATE r SET State=CASE WHEN r.State='Stopping' THEN 'Stopped' WHEN r.State='Interrupted' THEN 'Interrupted' ELSE 'Completed' END,FinishedAt=SYSUTCDATETIME()
        FROM dbo.AutomationRun r WHERE r.State IN ('Running','Draining','Stopping','Interrupted') AND r.FinishedAt IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.AutomationMessage m WHERE m.RunId=r.RunId AND m.State IN ('Queued','Sending','AwaitingDelivery'));
        UPDATE r SET State='Draining' FROM dbo.AutomationRun r WHERE r.State='Running' AND NOT EXISTS(SELECT 1 FROM dbo.AutomationMessage m WHERE m.RunId=r.RunId AND m.State='Queued');
        """, ct);
    public async Task<(Guid Run, int Sequence, string Source, string Payload, int Interval)?> Claim(CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct); await using var tx=(SqlTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd=new SqlCommand("""
            SELECT TOP(1) m.RunId,m.Sequence,r.SourceId,m.Payload,r.IntervalMs FROM dbo.AutomationRun r WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.AutomationMessage m WITH(UPDLOCK,HOLDLOCK) ON m.RunId=r.RunId WHERE r.State='Running' AND m.State='Queued' ORDER BY r.CreatedAt,m.Sequence
            """,c,tx);
        (Guid Run,int Sequence,string Source,string Payload,int Interval)? item=null;
        await using(var reader=await cmd.ExecuteReaderAsync(ct)) { if(await reader.ReadAsync(ct)) item=(reader.GetGuid(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),reader.GetInt32(4)); }
        if(item is { } m) { await using var update=new SqlCommand("UPDATE dbo.AutomationMessage SET State='Sending',SentAt=SYSUTCDATETIME() WHERE RunId=@id AND Sequence=@seq",c,tx); update.Parameters.AddWithValue("@id",m.Run);update.Parameters.AddWithValue("@seq",m.Sequence);await update.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);return item;
    }
    public async Task Record(Guid id,int seq,int code,string body,CancellationToken ct)
    {
        var state=code switch {202=>"AwaitingDelivery",400=>"ValidationRejected",409=>"ConflictRejected",_=>"Unknown"};
        await using var c=Connection();await c.OpenAsync(ct);
        await using var cmd=new SqlCommand("""
            UPDATE dbo.AutomationMessage SET State=@state,HttpStatus=@code,Response=@body,AnalyzedAt=SYSUTCDATETIME(),
            CompletedAt=CASE WHEN @state='AwaitingDelivery' THEN NULL ELSE SYSUTCDATETIME() END,
            Matched=CASE WHEN @state IN ('AwaitingDelivery','Unknown') THEN NULL WHEN Expected=@state THEN 1 ELSE 0 END WHERE RunId=@id AND Sequence=@seq
            """,c);
        cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@seq",seq);cmd.Parameters.AddWithValue("@code",code);cmd.Parameters.AddWithValue("@body",body);cmd.Parameters.AddWithValue("@state",state);await cmd.ExecuteNonQueryAsync(ct);
    }
}
