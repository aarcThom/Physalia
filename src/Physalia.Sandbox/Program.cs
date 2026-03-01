using Physalia.Core.Config;

var keysPath = "C:/Users/rober/repos/Physalia/src/Physalia.Sandbox/test.json";

Console.WriteLine($"Looking for keys at: {keysPath}");
Console.WriteLine();

var resolver = new ApiKeyResolver(keysPath);

// Test 1: Read a valid key
var claudeKey = resolver.GetKey("Claude Code");
Console.WriteLine($"Test 1 (Claude Code): {claudeKey}");
// Should print: sk-ant-test-12345

// Test 2: Read another valid key
var openAiKey = resolver.GetKey("OpenAI");
Console.WriteLine($"Test 2 (OpenAI): {openAiKey}");
// Should print: sk-openai-test-67890

// Test 3: Provider not in file — should throw
try
{
    resolver.GetKey("Groq");
}
catch (KeyNotFoundException ex)
{
    Console.WriteLine($"Test 3 (missing provider): {ex.Message} - GOOD");
}

// Test 4: List providers that have real keys (not YOUR_ placeholders)
var available = resolver.GetAvailableProviders();
Console.WriteLine($"Test 4 (available providers): {string.Join(", ", available)}");
// Should print: Claude Code, OpenAI   (DeepSeek filtered out because it starts with YOUR_)