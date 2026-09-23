using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Capstone.Core;

public sealed record MessageField(string Section, string Name, string Type, bool Required = true,
    int Min = 0, int Max = 100, string[]? Choices = null);
public sealed record MessageDefinition(string Name, MessageField[] Fields);
public sealed record GenerationOptions(int Count = 10, int ErrorPercent = 30, int Seed = 5910);
public sealed record FieldIssue(string Path, string Code);
public sealed record GeneratedMessage(int Sequence, JsonObject Payload, string? InjectedFault, bool ExpectedValid);

public static partial class MessageDefinitions
{
    [GeneratedRegex("\\A[A-Za-z][A-Za-z0-9_]{0,39}\\z")]
    private static partial Regex Identifier();

    public static string? Check(MessageDefinition? definition)
    {
        if (definition is null || definition.Name is null || !Identifier().IsMatch(definition.Name)) return "Use a type name starting with a letter, up to 40 letters, digits or underscores.";
        if (definition.Fields is not { Length: >= 1 and <= 24 }) return "Define 1 to 24 fields.";
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in definition.Fields)
        {
            if (f is null || f.Section is null || f.Name is null || !Identifier().IsMatch(f.Section) || !Identifier().IsMatch(f.Name)) return "Section and field names must start with a letter and contain up to 40 letters, digits or underscores.";
            if (!paths.Add(f.Section + "." + f.Name)) return "Field names must be unique within each section.";
            if (f.Type is not ("text" or "integer" or "boolean" or "identifier" or "choice")) return "Unsupported field type.";
            if (f.Type == "integer" && (f.Min < -1000000 || f.Max > 1000000 || f.Min > f.Max)) return "Integer bounds must be ordered and within -1000000 to 1000000.";
            if (f.Type == "text" && (f.Min < 0 || f.Max > 256 || f.Min > f.Max)) return "Text length bounds must be ordered and within 0 to 256.";
            if (f.Type == "choice" && (f.Choices is not { Length: >= 1 and <= 20 } || f.Choices.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64) || f.Choices.Distinct(StringComparer.Ordinal).Count() != f.Choices.Length)) return "Choice fields need 1 to 20 distinct, nonempty values of at most 64 characters.";
        }
        return null;
    }

    public static List<FieldIssue> Validate(MessageDefinition definition, JsonObject payload)
    {
        var issues = new List<FieldIssue>();
        foreach (var section in payload)
        {
            var fields = definition.Fields.Where(x => x.Section == section.Key).ToArray();
            if (fields.Length == 0) { issues.Add(new(section.Key, "unknown_section")); continue; }
            if (section.Value is not JsonObject obj) { issues.Add(new(section.Key, "section_must_be_object")); continue; }
            foreach (var field in obj)
                if (!fields.Any(x => x.Name == field.Key)) issues.Add(new(section.Key + "." + field.Key, "unknown_field"));
        }
        foreach (var f in definition.Fields)
        {
            var path = f.Section + "." + f.Name;
            if (payload[f.Section] is not JsonObject section || !section.TryGetPropertyValue(f.Name, out var node))
            { if (f.Required) issues.Add(new(path, "required")); continue; }
            if (node is not JsonValue value) { issues.Add(new(path, "invalid_type")); continue; }
            var valid = f.Type switch
            {
                "integer" => value.TryGetValue<int>(out var n) && n >= f.Min && n <= f.Max,
                "text" => value.TryGetValue<string>(out var text) && text.Length >= f.Min && text.Length <= f.Max,
                "boolean" => value.TryGetValue<bool>(out _),
                "identifier" => value.TryGetValue<string>(out var id) && Guid.TryParseExact(id, "D", out var guid) && guid != Guid.Empty,
                "choice" => value.TryGetValue<string>(out var choice) && f.Choices!.Contains(choice, StringComparer.Ordinal),
                _ => false
            };
            if (!valid) issues.Add(new(path, "invalid_" + f.Type));
        }
        return issues;
    }

    public static bool ValidOptions(GenerationOptions o) => o.Count is >= 1 and <= 1000 && o.ErrorPercent is >= 0 and <= 80;

    public static IReadOnlyList<GeneratedMessage> Generate(MessageDefinition definition, GenerationOptions options)
    {
        if (Check(definition) is not null || !ValidOptions(options)) throw new ArgumentException("Invalid definition or generation settings.");
        var random = new Random(options.Seed);
        var positions = Enumerable.Range(0, options.Count).ToArray();
        random.Shuffle(positions);
        var bad = positions.Take((int)Math.Round(options.Count * options.ErrorPercent / 100d, MidpointRounding.AwayFromZero)).ToHashSet();
        var messages = new List<GeneratedMessage>();
        for (var i = 0; i < options.Count; i++)
        {
            var payload = new JsonObject();
            foreach (var f in definition.Fields)
            {
                if (payload[f.Section] is not JsonObject) payload[f.Section] = new JsonObject();
                JsonNode value = f.Type switch
                {
                    "integer" => JsonValue.Create(random.Next(f.Min, f.Max + 1)),
                    "boolean" => JsonValue.Create(random.Next(2) == 1),
                    "identifier" => JsonValue.Create(Guid.NewGuid().ToString()),
                    "choice" => JsonValue.Create(f.Choices![random.Next(f.Choices.Length)]),
                    _ => JsonValue.Create(new string(Enumerable.Range(0, random.Next(f.Min, f.Max + 1)).Select(_ => (char)random.Next('A', 'Z' + 1)).ToArray()))
                };
                payload[f.Section]![f.Name] = value;
            }
            string? fault = null;
            if (bad.Contains(i))
            {
                var f = definition.Fields[random.Next(definition.Fields.Length)];
                // Null is invalid for every present field, even when omission is allowed.
                payload[f.Section]![f.Name] = null;
                fault = f.Section + "." + f.Name + ": injected null";
            }
            messages.Add(new(i + 1, payload, fault, fault is null));
        }
        return messages;
    }
}
