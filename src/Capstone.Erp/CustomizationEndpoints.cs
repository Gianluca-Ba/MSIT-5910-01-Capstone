using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capstone.Core;
using Microsoft.Data.SqlClient;

namespace Capstone.Erp;

public sealed record SavedDefinition(string Name, int Version, MessageDefinition Definition);
public sealed record ValidationEvidence(int Sequence, JsonObject Payload, string? InjectedFault, bool? ExpectedValid,
    bool ActualValid, bool? Matched, List<FieldIssue> Issues, DateTimeOffset RecordedAt);
public sealed record ValidationBatch(Guid BatchId, string Name, int Version, string Mode, GenerationOptions? Options,
    DateTimeOffset CreatedAt, IReadOnlyList<ValidationEvidence> Messages);

public sealed class CustomizationStore(IConfiguration config)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private SqlConnection Connection() => new(config.GetConnectionString("Database"));
    private static void Identity(SqlCommand cmd, string source, string name, int version)
    {
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@version", version);
    }

    public async Task<SavedDefinition> Save(string source, MessageDefinition definition, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var cmd = new SqlCommand("""
            DECLARE @next int=(SELECT ISNULL(MAX(Version),0)+1 FROM dbo.CustomMessageDefinition WITH(UPDLOCK,HOLDLOCK) WHERE SourceId=@source AND Name=@name);
            INSERT dbo.CustomMessageDefinition(SourceId,Name,Version,Definition) VALUES(@source,@name,@next,@definition);
            SELECT @next;
            """, c, tx);
        cmd.Parameters.AddWithValue("@source", source); cmd.Parameters.AddWithValue("@name", definition.Name);
        cmd.Parameters.AddWithValue("@definition", JsonSerializer.Serialize(definition, Json));
        var version = (int)(await cmd.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return new(definition.Name, version, definition);
    }

    public async Task<List<SavedDefinition>> List(string source, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Name,Version,Definition FROM dbo.CustomMessageDefinition WHERE SourceId=@source ORDER BY Name,Version DESC", c);
        cmd.Parameters.AddWithValue("@source", source);
        var result = new List<SavedDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0), reader.GetInt32(1), JsonSerializer.Deserialize<MessageDefinition>(reader.GetString(2), Json)!));
        return result;
    }

    public async Task<MessageDefinition?> Get(string source, string name, int version, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Definition FROM dbo.CustomMessageDefinition WHERE SourceId=@source AND Name=@name AND Version=@version", c);
        Identity(cmd, source, name, version);
        return await cmd.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<MessageDefinition>(json, Json) : null;
    }

    public async Task Record(string source, ValidationBatch batch, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT dbo.CustomValidationBatch(BatchId,SourceId,Name,Version,Evidence) VALUES(@id,@source,@name,@version,@json)", c);
        Identity(cmd, source, batch.Name, batch.Version);
        cmd.Parameters.AddWithValue("@id", batch.BatchId); cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(batch, Json));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> Read(string source, Guid? id, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(id.HasValue
            ? "SELECT Evidence FROM dbo.CustomValidationBatch WHERE SourceId=@source AND BatchId=@id"
            : "SELECT TOP(20) BatchId AS batchId,Name AS name,Version AS version,CreatedAt AS createdAt FROM dbo.CustomValidationBatch WHERE SourceId=@source ORDER BY CreatedAt DESC FOR JSON PATH", c);
        cmd.Parameters.AddWithValue("@source", source);
        if (id.HasValue) cmd.Parameters.AddWithValue("@id", id.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var json = new System.Text.StringBuilder();
        while (await reader.ReadAsync(ct)) json.Append(reader.GetString(0));
        return json.Length == 0 ? null : json.ToString();
    }
}

public static class CustomizationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/message-types", async (HttpContext ctx, CustomizationStore store, CancellationToken ct) => Results.Ok(await store.List(ServiceSetup.Source(ctx), ct)));
        app.MapPost("/api/message-types", async (MessageDefinition definition, HttpContext ctx, CustomizationStore store, CancellationToken ct) =>
        {
            var error = MessageDefinitions.Check(definition);
            return error is null ? Results.Ok(await store.Save(ServiceSetup.Source(ctx), definition, ct)) : Results.BadRequest(new { error });
        });
        app.MapPost("/api/message-types/{name}/{version:int}/generate", async (string name, int version, GenerationOptions options, HttpContext ctx, CustomizationStore store, CancellationToken ct) =>
        {
            if (!MessageDefinitions.ValidOptions(options)) return Results.BadRequest(new { error = "Use 1–1000 messages and 0–80% injected errors." });
            var source = ServiceSetup.Source(ctx);
            var definition = await store.Get(source, name, version, ct);
            if (definition is null) return Results.NotFound();
            var records = MessageDefinitions.Generate(definition, options).Select(m =>
            {
                var issues = MessageDefinitions.Validate(definition, m.Payload);
                return new ValidationEvidence(m.Sequence, m.Payload, m.InjectedFault, m.ExpectedValid, issues.Count == 0, m.ExpectedValid == (issues.Count == 0), issues, DateTimeOffset.UtcNow);
            }).ToArray();
            var batch = new ValidationBatch(Guid.NewGuid(), name, version, "GeneratedValidation", options, DateTimeOffset.UtcNow, records);
            await store.Record(source, batch, ct);
            return Results.Ok(batch);
        });
        app.MapPost("/api/message-types/{name}/{version:int}/validate", async (string name, int version, JsonObject payload, HttpContext ctx, CustomizationStore store, CancellationToken ct) =>
        {
            var source = ServiceSetup.Source(ctx);
            var definition = await store.Get(source, name, version, ct);
            if (definition is null) return Results.NotFound();
            var issues = MessageDefinitions.Validate(definition, payload);
            var batch = new ValidationBatch(Guid.NewGuid(), name, version, "ManualValidation", null, DateTimeOffset.UtcNow,
                [new(1, payload, null, null, issues.Count == 0, null, issues, DateTimeOffset.UtcNow)]);
            await store.Record(source, batch, ct);
            return Results.Ok(batch);
        });
        app.MapGet("/api/custom-batches", async (HttpContext ctx, CustomizationStore store, CancellationToken ct) => Results.Content(await store.Read(ServiceSetup.Source(ctx), null, ct) ?? "[]", "application/json"));
        app.MapGet("/api/custom-batches/{id:guid}", async (Guid id, HttpContext ctx, CustomizationStore store, CancellationToken ct) =>
        {
            var json = await store.Read(ServiceSetup.Source(ctx), id, ct);
            return json is null ? Results.NotFound() : Results.Content(json, "application/json");
        });
    }
}
