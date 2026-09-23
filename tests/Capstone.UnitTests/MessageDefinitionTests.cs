using Capstone.Core;
using System.Text.Json.Nodes;
using Xunit;

namespace Capstone.UnitTests;

public class MessageDefinitionTests
{
    private static MessageDefinition Definition => new("Notice", [
        new("Header", "Id", "identifier"),
        new("Details", "Qty", "integer", true, 1, 10),
        new("Details", "Area", "choice", true, Choices: ["A", "B"]),
        new("Details", "Label", "text", false, 1, 5),
        new("Details", "Enabled", "boolean", false)]);
    private static JsonObject Valid => JsonNode.Parse("""{"Header":{"Id":"e3d198f6-0bb4-4e9a-8a1f-dc91a107d00a"},"Details":{"Qty":1,"Area":"A"}}""")!.AsObject();

    [Fact] public void Known_valid_payload_allows_absent_optional_fields() => Assert.Empty(MessageDefinitions.Validate(Definition, Valid));
    [Theory]
    [InlineData("0", "invalid_integer")]
    [InlineData("11", "invalid_integer")]
    [InlineData("1.5", "invalid_integer")]
    [InlineData("\"1\"", "invalid_integer")]
    [InlineData("null", "invalid_type")]
    public void Quantity_contract_is_enforced(string value, string code)
    {
        var payload = Valid; payload["Details"]!["Qty"] = JsonNode.Parse(value);
        Assert.Contains(MessageDefinitions.Validate(Definition, payload), x => x.Path == "Details.Qty" && x.Code == code);
    }
    [Fact] public void Unknown_fields_and_missing_required_fields_are_reported()
    {
        var payload = Valid; payload["Details"]!.AsObject().Remove("Qty"); payload["Details"]!["Unexpected"] = true;
        var errors = MessageDefinitions.Validate(Definition, payload);
        Assert.Contains(errors, x => x.Code == "required"); Assert.Contains(errors, x => x.Code == "unknown_field");
    }
    [Fact] public void Wrong_section_shape_is_rejected()
    {
        var payload = Valid; payload["Details"] = 3;
        Assert.Contains(MessageDefinitions.Validate(Definition, payload), x => x.Code == "section_must_be_object");
    }
    [Fact] public void Optional_present_fields_still_validate()
    {
        var payload = Valid; payload["Details"]!["Label"] = "toolong"; payload["Details"]!["Enabled"] = "true";
        Assert.Equal(2, MessageDefinitions.Validate(Definition, payload).Count);
    }
    [Fact] public void Invalid_definitions_are_rejected()
    {
        Assert.NotNull(MessageDefinitions.Check(new("Invalid name", Definition.Fields)));
        Assert.NotNull(MessageDefinitions.Check(new("Notice", [new("A", "B", "integer", Min: 10, Max: 1)])));
        Assert.NotNull(MessageDefinitions.Check(new("Notice", [new("A", "B", "choice", Choices: ["x", "x"])])));
        Assert.NotNull(MessageDefinitions.Check(new("Notice", [Definition.Fields[0], Definition.Fields[0]])));
    }
    [Fact] public void Thousand_messages_contain_exactly_three_hundred_known_faults()
    {
        var generated = MessageDefinitions.Generate(Definition, new(1000, 30, 5910));
        Assert.Equal(300, generated.Count(x => !x.ExpectedValid));
        Assert.All(generated, m => Assert.Equal(m.ExpectedValid, MessageDefinitions.Validate(Definition, m.Payload).Count == 0));
        Assert.All(generated.Where(x => !x.ExpectedValid), m => Assert.Single(MessageDefinitions.Validate(Definition, m.Payload)));
    }
    [Fact] public void Seed_repeats_business_values_and_fault_positions()
    {
        var first = MessageDefinitions.Generate(Definition, new()); var second = MessageDefinitions.Generate(Definition, new());
        Assert.Equal(first.Select(x => x.InjectedFault), second.Select(x => x.InjectedFault));
        Assert.Equal(first.Select(x => x.Payload["Details"]!.ToJsonString()), second.Select(x => x.Payload["Details"]!.ToJsonString()));
    }
}
