using System.Text.Json.Serialization;

namespace Physalia.Core.Parsing;

public class ScriptResponse
{
    [JsonPropertyName("script")]
    public string Script { get; set; } = string.Empty;

    [JsonPropertyName("inputs")]
    public List<ParamDefinition> Inputs { get; set; } = new();

    [JsonPropertyName("outputs")]
    public List<ParamDefinition> Outputs { get; set; } = new();

}

public class ParamDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("prettyName")]
    public string? PrettyName { get; set; }

    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; set; }

    [JsonPropertyName("typeHint")]
    public string? TypeHint { get; set; }

    [JsonPropertyName("access")]
    public string Access { get; set; } = "item";

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}