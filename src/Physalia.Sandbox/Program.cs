using Physalia.Core;
using Physalia.Core.Config;
using Physalia.Core.Providers;

// Setup
var keysPath = "C:/test.json";
var resolver = new ApiKeyResolver(keysPath);
var apiKey = resolver.GetKey("Claude Code");
var provider = new AnthropicProvider();

// One object, one call
var client = new PhysaliaClient(provider, apiKey);

Console.WriteLine("Sending prompt...");
Console.WriteLine();

var result = await client.GenerateScriptAsync(
    "Create a spiral curve. Inputs: center point, number of turns, radius, and height. Output: the spiral as a curve."
);

Console.WriteLine("=== SCRIPT ===");
Console.WriteLine(result.Script);
Console.WriteLine();

Console.WriteLine("=== INPUTS ===");
foreach (var input in result.Inputs)
{
    Console.WriteLine($"  {input.Name} ({input.TypeHint}, {input.Access})");
}

Console.WriteLine();
Console.WriteLine("=== OUTPUTS ===");
foreach (var output in result.Outputs)
{
    Console.WriteLine($"  {output.Name} ({output.PrettyName})");
}