using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
namespace Capstone.Core;

public sealed class SqlStore(string connectionString)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var c = new SqlConnection(connectionString);
        try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; }
    }
    private static SqlCommand Command(SqlConnection c, SqlTransaction? tx, string sql, string source, Guid id)
    {
        var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 10 };
        cmd.Parameters.Add("@source", SqlDbType.NVarChar, 64).Value = source;
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        return cmd;
    }
    public async Task<bool> Healthy(CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.SchemaVersion WHERE Version=1", c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
    public async Task<OrderStatus> Submit(string source, OrderRequest order, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var find = Command(c, tx, "SELECT Payload FROM dbo.SourceOrder WITH (UPDLOCK,HOLDLOCK) WHERE SourceId=@source AND OrderId=@id", source, order.OrderId);
        var payload = await find.ExecuteScalarAsync(ct) as string;
        if (payload is not null)
        {
            if (!OrderRules.SameContent(JsonSerializer.Deserialize<OrderRequest>(payload, Json)!, order)) throw new OrderConflictException();
            await tx.CommitAsync(ct);
            return (await Get(source, order.OrderId, ct))!;
        }
        await using var insert = Command(c, tx, """
            INSERT dbo.SourceOrder(SourceId,OrderId,Payload) VALUES(@source,@id,@payload);
            INSERT dbo.OutboxMessage(SourceId,OrderId,Status) VALUES(@source,@id,'Pending');
            """, source, order.OrderId);
        insert.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(order, Json);
        await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new(source, order, "Pending", null);
    }
    public async Task<OrderStatus?> Get(string source, Guid id, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = Command(c, null, """
            SELECT o.Payload,m.Status,m.Receipt FROM dbo.SourceOrder o
            JOIN dbo.OutboxMessage m ON m.SourceId=o.SourceId AND m.OrderId=o.OrderId
            WHERE o.SourceId=@source AND o.OrderId=@id
            """, source, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new(source, JsonSerializer.Deserialize<OrderRequest>(r.GetString(0), Json)!, r.GetString(1), r.IsDBNull(2) ? null : JsonSerializer.Deserialize<Receipt>(r.GetString(2), Json));
    }
    public async Task<(string Source, OrderRequest Order)?> Next(CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP(1) o.SourceId,o.Payload FROM dbo.OutboxMessage m
            JOIN dbo.SourceOrder o ON o.SourceId=m.SourceId AND o.OrderId=m.OrderId
            WHERE m.Status='Pending' ORDER BY m.CreatedAt,m.SourceId,m.OrderId
            """, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetString(0), JsonSerializer.Deserialize<OrderRequest>(r.GetString(1), Json)!);
    }
    public async Task Complete(string source, Guid id, string status, string category, Receipt? receipt, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx, """
            UPDATE dbo.OutboxMessage SET Status=@status,Receipt=@receipt
            WHERE SourceId=@source AND OrderId=@id AND Status='Pending';
            IF @@ROWCOUNT=1 INSERT dbo.DeliveryAttempt(SourceId,OrderId,Outcome) VALUES(@source,@id,@category);
            """, source, id);
        cmd.Parameters.Add("@status", SqlDbType.VarChar, 32).Value = status;
        cmd.Parameters.Add("@category", SqlDbType.VarChar, 64).Value = category;
        cmd.Parameters.Add("@receipt", SqlDbType.NVarChar, -1).Value = receipt is null ? DBNull.Value : JsonSerializer.Serialize(receipt, Json);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<AcceptanceEvidence?> GetAcceptance(string source, Guid id, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = Command(c, null, """
            SELECT o.Payload,r.Receipt,
              (SELECT COUNT(*) FROM dbo.AcceptedOrder WHERE SourceId=@source AND OrderId=@id),
              (SELECT COUNT(*) FROM dbo.AcceptanceReceipt WHERE SourceId=@source AND OrderId=@id)
            FROM dbo.AcceptedOrder o JOIN dbo.AcceptanceReceipt r
              ON r.SourceId=o.SourceId AND r.OrderId=o.OrderId
            WHERE o.SourceId=@source AND o.OrderId=@id
            """, source, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new(JsonSerializer.Deserialize<OrderRequest>(r.GetString(0), Json)!,
            JsonSerializer.Deserialize<Receipt>(r.GetString(1), Json)!, r.GetInt32(2), r.GetInt32(3));
    }
    public async Task<Acceptance> Accept(string source, OrderRequest order, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Key-range update locks serialize concurrent first deliveries for the same identity.
        await using var find = Command(c, tx, "SELECT Payload FROM dbo.AcceptedOrder WITH (UPDLOCK,HOLDLOCK) WHERE SourceId=@source AND OrderId=@id", source, order.OrderId);
        var payload = await find.ExecuteScalarAsync(ct) as string;
        if (payload is not null)
        {
            if (!OrderRules.SameContent(JsonSerializer.Deserialize<OrderRequest>(payload, Json)!, order)) throw new OrderConflictException();
            await using var lookup = Command(c, tx, "SELECT Receipt FROM dbo.AcceptanceReceipt WHERE SourceId=@source AND OrderId=@id", source, order.OrderId);
            var stored = (string)(await lookup.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Missing receipt"));
            await tx.CommitAsync(ct);
            return new(JsonSerializer.Deserialize<Receipt>(stored, Json)!, true);
        }
        var receipt = new Receipt(source, order.OrderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using var insert = Command(c, tx, """
            INSERT dbo.AcceptedOrder(SourceId,OrderId,Payload) VALUES(@source,@id,@payload);
            INSERT dbo.AcceptanceReceipt(SourceId,OrderId,Receipt) VALUES(@source,@id,@receipt);
            """, source, order.OrderId);
        insert.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(order, Json);
        insert.Parameters.Add("@receipt", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(receipt, Json);
        await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new(receipt, false);
    }
}
