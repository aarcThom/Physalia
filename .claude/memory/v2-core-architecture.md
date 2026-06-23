# Physalia v2 Core Architecture

Decisions locked via grilling session 2026-05-03.

---

## Core Boundary Rule
GH owns all mutable state. `Physalia.Core` is a pure functional library — no side effects, no GH dependency.

---

## Conversation Model

```csharp
public enum Role { User, Assistant }  // Tool added when tool-calls land

public abstract record MessageContent;
public record TextContent(string Text) : MessageContent;
public record ImageContent(ImageSource Source) : MessageContent;
// Future: ToolCallContent, ToolResultContent

public abstract record ImageSource;
public record InlineImage(byte[] Data, string MimeType) : ImageSource;
public record UrlImage(string Url) : ImageSource;
public record ManagedImage(string FileHandle) : ImageSource;

public record ConversationMessage(Role Role, IReadOnlyList<MessageContent> Content);
```

- A single `ConversationMessage` carries multiple content blocks (text + image together is valid)
- `Conversation` is a **Core class** (not a record) with `Append(ConversationMessage)` returning a new `Conversation` and enforcing invariants (e.g. no consecutive same-role turns)
- GH replaces its reference on each append — Core never mutates in place
- System prompt is a `string` passed at call time, NOT stored in `Conversation`
- Images travel inside `ConversationMessage` (not as a side-channel to Reasoner)

---

## Provider Hierarchy

Three-level: protocol abstract base → named concrete provider. Abstract classes (not interfaces) because they share `HttpClient` state.

```
OpenAIProtocolProvider (abstract)   — wire format + HTTP once
    OpenAIProvider
    DeepSeekProvider      // overrides: thinking-mode param stripping, reasoning_content
    OllamaProvider        // overrides: keep_alive, streaming default
    OpenRouterProvider    // adds: provider routing params
    GroqProvider

AnthropicProtocolProvider (abstract)
    AnthropicProvider

GeminiProtocolProvider (abstract)
    GeminiProvider
```

Provider-specific quirks live in the named subclass. Wire format logic lives once in the abstract base.

---

## ModelConfig Hierarchy

Mirrors the provider hierarchy exactly. The GH `Model` component pattern-matches on the concrete type to autopopulate its inputs for that provider.

```
ModelConfig (abstract record)
    OpenAIProtocolConfig (abstract record)
        OpenAIConfig, DeepSeekConfig, OllamaConfig, OpenRouterConfig, GroqConfig
    AnthropicProtocolConfig (abstract record)
        AnthropicConfig
    GeminiProtocolConfig (abstract record)
        GeminiConfig
```

Common params (ModelId, ApiKey, MaxTokens) on the base. Protocol-level params (Temperature, TopP, TopK) on protocol abstract. Provider-specific params (keep_alive, thinking, reasoning_effort) on named config.

---

## Result, Errors, Response Types

```csharp
// Rolled our own — no external dependency
Result<T, E>  // two-case discriminated union: Ok(T) | Err(E)

public record LlmError(LlmErrorKind Kind, string Message);
public enum LlmErrorKind { Network, Auth, RateLimit, InvalidRequest, Timeout, Cancelled }

public record LlmResponseChunk(string? ContentDelta, bool IsLast, LlmUsage? Usage);
public record LlmUsage(int InputTokens, int OutputTokens);
// LlmUsage arrives on the final chunk (IsLast = true)
// Aggregating to full string is an extension method
```

---

## Provider Interface (Streaming)

```csharp
IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
    Conversation conversation,
    string systemPrompt,
    ModelConfig config,
    CancellationToken ct);
```

---

## Validation (Auditor)

Pure function: `(string json, string schema) → Result<string, ValidationError>`

```csharp
public record ValidationError(string Message, IReadOnlyList<SchemaViolation> Violations);
public record SchemaViolation(string Path, string Message);
```

Structured violations give the LLM actionable feedback (field path + message).

---

## ApiKeyResolver — Resolution Order

1. Environment variable (convention wins)
2. YAML config file (`API_KEY_CONFIG.YAML`)

No third fallback — fail explicitly if neither source has the key.

---

## Namespace Structure

```
Physalia.Core/
    Conversation/    ← Role, MessageContent (+subtypes), ImageSource (+subtypes),
                       ConversationMessage, Conversation
    Providers/
        Protocol/    ← OpenAIProtocolProvider, AnthropicProtocolProvider,
                       GeminiProtocolProvider (abstract)
        Named/       ← OpenAIProvider, AnthropicProvider, DeepSeekProvider,
                       OllamaProvider, OpenRouterProvider, GroqProvider, GeminiProvider
    Models/
        Protocol/    ← OpenAIProtocolConfig, AnthropicProtocolConfig,
                       GeminiProtocolConfig (abstract records)
        Named/       ← OpenAIConfig, AnthropicConfig, DeepSeekConfig,
                       OllamaConfig, OpenRouterConfig, GroqConfig, GeminiConfig
    Common/          ← Result<T,E>, LlmError, LlmErrorKind,
                       LlmResponseChunk, LlmUsage
    Prompts/         ← SystemPrompt, PromptHelpers
    Validation/      ← SchemaValidator, ValidationError, SchemaViolation
    Config/          ← ApiKeyResolver, LlmProviderFactory
```
