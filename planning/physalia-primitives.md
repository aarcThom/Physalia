# Physalia Primitives

A reference for the core components of the Physalia plugin. This is (*mostly*) not a description of the underlying architecture, but rather, the Grasshopper components themselves. Components marked **LLM-driven** accept a model API as a parameter and make inference calls. All others are deterministic.

---

## Component Lifecycle

Pipeline components share an explicit state machine (`StatefulComponentBase`) so data can be *seen* flowing through the DAG. Events travel as **Signals** — latched, sequence-numbered values consumed exactly once by each receiver — never as momentary bool pulses (see `data-marshalling.md` for the full model):

| State | Success Signal | Fail Signal | Canvas caption |
|---|---|---|---|
| **empty** | none | none | *(blank)* |
| **active** | none (cleared on entry) | none | `Active…` |
| **solve success** | latched (minted once; payload = result) | none | `Success` |
| **solve failure** | none | latched (minted once; payload = feedback) | `Failed` |

- The signal **is** the data carrier: its payload holds the result string on success or the feedback string on failure, so each hop between pipeline components is one wire. Signals cast to text (the payload) for native GH interop; **Deconstruct Signal** taps any wire passively and **Construct Signal** mints one by hand.
- A component is **empty** when fresh on canvas, never yet run, manually cleared via its right-click Clear item, or freshly loaded from a file (lifecycle state never persists).
- Consuming an incoming signal enters **active**: stale outgoing signals blank immediately, the component does its work (instant for Auditor, an API call for Reasoner), then holds a visible delay (`SolveDelayMs`, currently 500 ms, wall-clock honest) before latching, so the hop is traceable by eye. Signals arriving while busy wait on the wire and are serviced afterwards — nothing is ever dropped.
- The outcome latches into **solve success** or **solve failure**: one signal is minted carrying the payload, and it persists until the next run or a clear. Downstream components consume the signal exactly once — recomputes and replays never re-fire a chain, and processing order follows the global sequence (causal order), not solve timing.
- Signal inputs accept only signals; a bare Button/Toggle has no payload and is a hard error there. Multiple signal sources wire into one input directly — no OR gates. For a manual run, use Construct Signal, whose dedicated native Boolean Trigger input mints a payload-carrying signal on each Button press.

Routing components (Reasoner, Auditor, Transmitter, Schema Translator) layer the standard contract on top: Signal in (payload = working data), Success Signal out, Fail Signal out. Recorder participates in the same state machine with dedicated Prompt/Response/Feedback Signal inputs and emits its signal only on user-turn appends (see below).

---

## Core Pipeline

### Composer
**Role:** Assembles the system prompt from modular parts at runtime from either ***Library*** component or plain string inputs. Accepts discrete inputs — preamble, format instructions (from schema), plugin list, tool descriptions, reference images — and concatenates them into a single instruction passed to Recorder.

**Deterministic.**

**I/O:**

Inputs:
- `preamble` — string
- `schema` — string
- `tool descriptions` — string, optional


Output:
- `system prompt` — string

Right Clicks:
- `Save new .composer` — saves new .composer file
- `Append to .composer` -- saves to default .composer file

---

### Prompter
**Role:** The sole entry point for human intent into the pipeline. Accepts user text input and emits it as a `PromptInfo` to Recorder. In its simplest form a GH text parameter with a button trigger. In its most complete form the Eto chat window — but either way it is a distinct primitive because it is the only component whose trigger is a human, not a pipeline signal. Prompter owns nothing except the act of capturing and forwarding user input; it does not manage history, does not modify the system prompt, and does not interpret the input in any way.

The `reference` input is a simple filepath and is used to reference images.

**Deterministic.**

**I/O:**

Inputs:
- `UI interface` — Physalia styled panel as per v.0.1.
- `Submit` - A boolean button input.
- `reference` — file path string, optional
- `inputs outputs` — inputs and outputs ref'd from a receiver via monitor - optional.

Outputs:
- `prompt` — string
- `reference` — file path string, optional, passed through to Recorder
- `trigger` — boolean, fires on user submission, initiates downstream solve

Right Click:
- `Open Chat` - Opens the eto forms chat window that can be used as an alternative UI.

---

### Recorder
**Role:** Maintains the full conversation history as an append-only log. Every component in the pipeline that produces an observation — errors, user input, perception output, system prompt — appends to Recorder. It is the sole source of truth the Reasoner sees on each call. Recorder is the only component that understands the feedback topology — it arbitrates between forward data flow and incoming Feedback signals, blocking one when the other is active.

**Deterministic.**

**I/O:**

Inputs:
- `system prompt` — string from Composer
- `prompt` — string; recorded when a Prompt Signal with an empty payload arrives (e.g. a Button press)
- `prompt signal` — records a user turn (payload = prompt text, falls back to `prompt`)
- `response signal` — records an assistant turn (from Reasoner's Success Signal; Tool Calls take priority over the payload)
- `feedback signal` — records feedback as a user turn (from one or more Feedback Collectors, wired directly)
- `tool calls` — list, optional
- `conversation` — optional compacted conversation override

Outputs:
- `instructions` — system prompt + conversation bundled for inference, latched after each run
- `signal` — minted **only when a user message was appended/merged** (payload = the user text). Assistant turns latch quietly (no signal) so a Reasoner wired off this output cannot re-fire itself after its own response is recorded.
- `recorded history` — full conversation including all messages before and after compaction

**Lifecycle:** Recorder shares the component state machine but not the routing contract. It has three dedicated Signal inputs — `Prompt Signal`, `Response Signal`, `Feedback Signal` — so the turn type comes from event identity, never from conversation parity. Waiting signals are consumed in global sequence order (causal order), guaranteeing a response is recorded before the feedback it provoked even when both arrive in the same solve. User-side text arriving when the last turn is already a user message merges into that message (providers require role alternation). Appends happen on the consume solve; outputs latch after the visible delay. A signal with nothing new to record latches as a quiet failure with a warning.

Right Click
- `Save Conversation` - saves conversation to .convo file in JSON format
- `Load Conversation` - Loads a conversation from .convo file
- `Clear Conversation` - Clears the current conversation and resets the component to empty

---

### Reasoner
**LLM-driven.**

**Role:** The core inference component. Receives the full conversation history from Recorder and performs a single forward pass to produce structured output — a JSON node graph, a Python script, or any other structured format defined by the active Composer configuration. Used in cunjunction with **Library** component if not hooked up to a recorder. Stateless between calls; all context lives in Recorder. The model API and any additional instruction are parameters.

**I/O:**

Inputs:
- `instructions` — typed Instructions (most probably from recorder or library); this is the context — the trigger signal's payload is ignored
- `model` — Model record, provider, model id, API key, inference parameters
- `cancel` - boolean from button - sends cancel token (a human abort, deliberately not a signal)
- `signal` — run signal from Recorder

Outputs:
- `success signal` — payload = raw LLM response; consumed by Auditor and Recorder
- `fail signal` — payload = API error text

**Alternate Use Cases**

These use cases are to be retrieved with the **Library** component.
- *Distiller* — Reasoner with a compaction instruction: "summarize this conversation history into a concise document state"
- *Reflector* — Reasoner with a reflection instruction: "explain what went wrong and what you will fix before retrying"
- *Interpreter* — Reasoner with a vision-capable model and instruction: "describe this image as a structured text observation"
- *Encoder* — Reasoner with instruction: "convert this structured geometry data into a plain English description"
- *Curator* — Reasoner with instruction: "extract only the unresolved issues from this observation"
- *Critic* — Reasoner with instruction: "evaluate this output against the following design criteria and return a pass/fail with reasoning"
- *Translator* — Reasoner with instruction: "translate this error message into a concise observation suitable for appending to conversation history"
- *Educator* - Reasoner with instruction: "explain what's happening with these components / code."

---

### Auditor
**Role:** Strips out all non-essential information from the LLM response (such as human friendly message, white spaces, etc.) and tests that the provided JSON matches the provided schema retrieved from a **Library** file. Returns either a successfully parsed json string sends an error message via feedback. Auditor first performs a parsing pass to detect structural problems with the JSON; it then performs a semantic validation via Libraries like NJsonSchema or JsonSchema.Net against the provided schema file.

**Deterministic**

**I/O:**

Inputs:
- `schema` — user-defined schema for runtime deserialization
- `signal` — from Reasoner's Success Signal; the payload is the raw LLM output to validate

Outputs:
- `success signal` — payload = properly formatted JSON string
- `fail signal` — payload = validation feedback (routed back via Feedback)

**Alternate Use Cases**

These use cases are to be retrieved with the **Library** component. The RulesEngine can enforce these.
- *Monitor* — Checks the the structural validity of connection validity.

---

### PyValidator
**Role:** A pre-assembly validator specific to the Python pipeline. Runs two sequential checks against the generated script before Receiver touches the GH document — static analysis via pyflakes, RhinoCommon type checking and a dry-run execution with typed dummy inputs. Only fires on the Python path; the Cluster path uses Auditor's rule schema for equivalent validation. Sits between Auditor and Transmitter in the Python pipeline.
 
**Deterministic.**
 
**I/O:**
 
Inputs:
- `data` — string, validated Python script JSON from Auditor
- `trigger` — boolean
Outputs:
- `data` — string, passed through unchanged if all checks pass
- `trigger` — boolean, passes through on full validation success
- `feedback` — The feedback object with error info with user as the role.

**Validation sequence:**
 
1. **Static analysis** — pyflakes via `RhinoCode.RunScript`. Input variable names injected as `name = None` stubs to suppress false-positive undefined-variable warnings. Catches syntax errors, undefined names, unused imports. Does not catch RhinoCommon type errors — those are caught in pass 2.
2. **Dry-run execution** — executes the script via `RhinoCode.RunScript` with typed dummy inputs derived from each input's `typeHint`. Because the real `Rhino.Geometry` is available in Rhino's embedded CPython runtime, RhinoCommon type errors (`Brop`, wrong method signatures, incorrect argument types) surface here as real `ImportError` or `AttributeError` exceptions — no stubs required. Catches runtime errors that static analysis cannot. Checks that all declared outputs are assigned after execution.
**Dummy input values by typeHint:**
 
| typeHint | Dummy value |
|---|---|
| Number | `1.0` |
| Integer | `1` |
| Boolean | `true` |
| Text | `"test"` |
| Point | `Point3d(0,0,0)` |
| Vector | `Vector3d(1,0,0)` |
| Plane | `Plane.WorldXY` |
| Line | `Line(Origin, Point3d(1,0,0))` |
| Circle | `Circle(1.0)` |
| Curve | `LineCurve(Origin, Point3d(1,0,0))` |
| Brep | `Brep.CreateFromBox(BoundingBox(Origin, Point3d(1,1,1)))` |
| Mesh | `Mesh.CreateFromBox(BoundingBox(...), 1,1,1)` |
| Surface | `NurbsSurface.CreateFromCorners(...)` |
| Box | `Box(Plane.WorldXY, Interval(0,1), Interval(0,1), Interval(0,1))` |
| Interval | `Interval(0,1)` |
| Colour | `Color.Red` |
 
For `list` access, each dummy value is wrapped in a single-item list.
 
**Important caveat on dry-run:** dummy inputs are minimal typed placeholders, not representative geometry. Scripts that depend on real geometry properties (mesh topology, surface curvature, object counts) will behave differently in dry-run than in real execution. Dry-run catches import and type errors only — it does not validate output correctness. Error messages from this pass should indicate they originate from a test execution with dummy inputs.
 
**Current implementation notes (v0.1.0):**
 
The existing `CodeChecker.cs` and `ScriptRunner.cs` cover static analysis and real execution respectively. The following improvements are worth making in v0.2:
 
- **`IsBlockingError` is fragile** — currently matches pyflakes output as raw strings. Pyflakes has stable message codes (`F821` undefined name, `E999` syntax error, `F401` unused import) that are more reliable than string matching. The parsing loop in `CodeChecker.cs` should be extended to capture and preserve these codes alongside line and column, then `IsBlockingError` filters on codes rather than message content.
- **Python is loaded twice** — `CodeChecker` and `ScriptRunner` both call `EnsurePythonLoaded()` independently. Combining them into `ScriptValidator` with a single shared `_pythonLoaded` flag eliminates the redundant load check and makes the load lifecycle explicit.
- **No type checking pass currently** — pyflakes alone does not catch RhinoCommon type errors. However since `RhinoCode.RunScript` runs inside Rhino's embedded CPython with real `Rhino.Geometry` available, the dry-run pass catches these errors directly as `ImportError` or `AttributeError` exceptions. No external stubs or type checker required — the real runtime is the type checker.
- **No dry-run currently** — `ScriptRunner.Execute` only runs with real GH inputs. Adding `DryRun()` as a pre-assembly pass with typed dummy inputs catches runtime errors before the document is touched. Unconnected inputs (the current gap) are handled entirely by this pass — every declared input receives a typed dummy value regardless of what is wired in GH.
- **`list` access only, no `tree`** — `ScriptRunner.CollectInputs` handles `item` and `list` but not `tree`. Data tree inputs currently fall through to the `item` path. A `DataTree<object>` dummy value should be added to the dry-run factory for completeness, and real tree collection should be added to `CollectInputs` for the execution path.

---

### Transmitter
**Role:** The junction point between the correctness layer and the execution layer. Receives validated output from Auditor and/or PyValidator. Has a two-directional connection with a Receiver component (represented by a galapagos style wire). Triggers the receiver to build and receives the output - errors, warnings, null output (if inputs present). Passes the output to feedback if error.

**Deterministic.**

**I/O:**

Inputs:
- `signal` — from Auditor's Success Signal; the payload is the validated JSON to push

Outputs:
- **galapagos style connector** - to receiver
- `success signal` — payload = the pushed code, on clean execution
- `fail signal` — payload = the target's runtime errors (routed back via Feedback)

Right Click:
- `save receiver config` - saves the receiver configuration to the library.
- `autosave config` - automatically saves the configuration upon successful completion.
- `clear receiver` - resets the attached receiver
- `detach receiver` - detaches the attached receiver

---

### Receiver
**Role:** The sole executor of side effects on the GH document. Consumes Transmitter output and performs component placement, parameter wiring, and script injection on GH_Document. The only component in the pipeline that mutates the Grasshopper environment. Uses passed JSON to determine whether to generate a cluster component or python component.

**Deterministic.**

**I/O:**

Inputs:
- **galapagos style connector 1** - connection to receiver
- **galapagos style connector 2** - connection to Monitor
- *Variable user inputs* - set by user

Outputs:
- *Variable outputs* - set by user

---

## Regulators

### Feedback
**Role:** Routes data wirelessly back to Feedback Collector without participating in GH's normal DAG execution model. Click-to-pair interaction connects a Feedback component to a target Feedback collector. When deselected, renders as a radio waves icon — the wire is hidden. When selected, reveals a pink wire to the paired Feedback Collector. Feedback is the only component that explicitly breaks GH's acyclic constraint; this is a deliberate design decision and the radio waves icon calls it out visually.

**Deterministic.**

**I/O:**

Inputs:
- `signal` — typically a Fail Signal; each consumed signal is forwarded as-is (original sequence and payload preserved)

Outputs:
- *(wireless)* — signals routed directly to paired FeedbackCollector(s), no physical output wire. 'Lights up' when triggered to indicate output

---

### Feedback Collector
**Role:** Feecback collector collects one or more feedback signals to feed-forward. Closes the loop to allow for cylical actions such as self correction.

**Deterministic.**

**I/O:**

Inputs:
- *(wireless)* — signals injected by one or more paired Feedback components, no physical input wire. 'Lights up' when triggered to indicate input

Outputs:
- `signal` — one minted signal per batch; payload = all feedback from the batch, newline-joined. Carries a Failure outcome if any injected signal was a failure.

---

## Signals Utilities

### Construct Signal
**Role:** Mints a signal carrying an arbitrary text payload, once per Button press — the manual entry point into the pipeline. A Panel of text plus a Button lets any signal-driven component (e.g. Auditor) be run standalone. The Trigger is a native Boolean input (not a Signal): it is the one sanctioned place a Button drives the pipeline, since Signal inputs themselves accept only signals. Latches immediately (a source, not a processing hop — no visible delay).

**Deterministic.**

**I/O:**

Inputs:
- `payload` — string carried by the minted signal
- `failure` — boolean; when true the minted signal carries a Failure outcome (for hand-testing feedback paths)
- `trigger` — native boolean; one mint per Button press (false→true), nothing on load/paste

Outputs:
- `signal` — the minted signal, latched until the next trigger or a clear

---

### Deconstruct Signal
**Role:** Breaks a signal into its fields for inspection or native-GH interop. Passive — it never consumes, so it can tap any signal wire without disturbing the consume-once bookkeeping of real receivers. (For just the payload, a signal wire also casts directly into any native text input.)

**Deterministic.**

**I/O:**

Inputs:
- `signal` — the signal to inspect

Outputs:
- `sequence` — integer, global causal order
- `success` — boolean outcome
- `payload` — string
- `source` — minting component's name
- `time` — local mint time

---

### Counter
**Role:** Counts discrete pass-throughs and blocks forward signal after a user-defined threshold. Prevents infinite feedback loops by enforcing a maximum retry count. When the threshold is reached, Counter blocks the signal and optionally emits a terminal ErrorInfo downstream.

**Deterministic.**

**I/O:**

Inputs:
- `data` — string, any Info type
- `threshold` — integer, maximum number of pass-throughs before blocking
- `trigger` — boolean from upstream component

Outputs:
- `data` — string, passed through unchanged while under threshold
- `trigger` — boolean, passes through while under threshold, blocked when threshold is reached
- `blocked` — boolean, fires when threshold is exceeded

---

### Meter
**Role:** Tracks cumulative token consumption against a user-defined budget threshold. Blocks forward signal when the budget is exceeded. Complements Counter — Counter tracks discrete attempts, Meter tracks continuous resource consumption. Critical for cost governance in BYOK deployments where users are responsible for their own API spend.

**Deterministic.**

**I/O:**

Inputs:
- `data` — string, any Info type
- `budget` — integer, maximum token count before blocking
- `trigger` — boolean from upstream component

Outputs:
- `data` — string, passed through unchanged while under budget
- `trigger` — boolean, passes through while under budget, blocked when budget is exceeded
- `blocked` — boolean, fires when budget is exceeded
---

## Perception

### Monitor
**Role:** Captures real-time information about a receiver (It's inner workings (components or code) and inputs and outputs.), grasshopper group, or entire GH document. 

Inputs:
- *(wireless)* — galapagos style wire connecting to receiver or group. No connection means reference entire document
- `trigger` — boolean from upstream component

Output:
- `data` — string, description of component(s)
- `trigger` — boolean, passes through


### Observer
**Role:** Captures viewport screenshots from the Rhino viewport. Output is raw image data passed to Interpreter (a Reasoner instance) for conversion to text before entering Recorder.

**Deterministic.**

**I/O:**

Inputs:
- `target` - passed in grasshopper Geo to force camera zoom to, optional
- `trigger` — boolean, user-initiated, not part of the automatic correctness loop

Outputs:
- `screenshot` — file path string, captured viewport image
- `trigger` — boolean, passes through on capture completion
---

## Configuration

Physalia will be bundled with a bunch of plain text files that a user can modify. The folder structure will look like this.

```
/Physalia
    /Runtime (the actual plugin)
    /Files
        API_KEY_CONFIG.YAML
        
        /SKILLS (contains prompts for reasoner)
            educator.skill
            distiller.skill
          
        /SCHEMAS (contains the schemas for auditor)
            python.schema
            cluster.schema
        
        /CLUSTERS (clusters the LLM can use)
            example1.ghcluster
            example2.ghcluster
            
        /RECEIVERS (physalia components the LLM can use)
            example1.receiver
            example2.receiver
```
### Library
**Role:** References files within the skills and schemas folders to pass values to Reasoner, Parser, Auditor. The files are formatted as JSON and **Library** adjusts its outputs as per specification in the file.

**I/O:**

Inputs:
- `folder` — path for the folder.
- `file` — outputs to value picker for user to select a file

Outputs:
- ***VARIES*** - output will depend on specs in file.

### Model
**Role:** A configuration record that carries everything needed to make an authenticated LLM API call — provider, model identifier, API key, and inference parameters. Wired as an input into any LLM-driven component (Reasoner, Router). API key resolution happens inside Model at instantiation from either a registered environment variable or a user-provided YAML file; downstream components receive a fully configured Model and call it without knowing where the key came from. Multiple Model components can exist in the same pipeline, each configured differently, enabling heterogeneous agent pipelines where different components use different providers or models.

**Deterministic.**

**I/O:**

Inputs:
- `provider` — string, e.g. "anthropic", "openai", "ollama"
- `model id` — string, e.g. "claude-sonnet-4-6", "gpt-4o"
- `yaml path` — file path string, optional, YAML file containing API key and defaults
- `temperature` — float, optional, defaults to provider default
- `top-p` — float, optional, defaults to provider default
- `max tokens` — integer, optional, defaults to provider default

Output:
- `model` — Model record, wired to any LLM-driven component

**Internal structure:**
```csharp
public record Model(
    string Provider,    // "anthropic", "openai", "ollama" etc.
    string ModelId,     // "claude-sonnet-4-6", "gpt-4o" etc.
    string ApiKey,      // resolved from environment or YAML
    float Temperature,
    float TopP,
    int MaxTokens
);
```

**Key resolution order:**
1. YAML file path wired directly into Model
2. Environment variable matching the provider convention
3. Physalia global settings fallback

---

## Utility

### Aggregator
**Role:** Combines multiple Info outputs from the perception layer into a single structured observation before passing to Recorder in one call. Ensures Recorder's interface stays simple — it always receives a single input per cycle regardless of how many perception components fired. Order and formatting of aggregated inputs is a parameter.

**Deterministic.**

**I/O:**

Inputs:
- `data` — string, N inputs from any upstream component
- `trigger` — boolean from upstream component

Outputs:
- `aggregated` — string, all inputs combined into a single structured observation
- `trigger` — boolean, passes through on completion

---

### Router
**LLM-driven.**

**Role:** Receives N inputs, evaluates them using a classification LLM call, and activates one of N output paths based on the decision. Structurally distinct from Reasoner — its job is to decide, not to generate. The LLM call inside Router is a classification call with minimal output (a path identifier), not a generation call. Well-suited to small local models regardless of what model is behind Reasoner.

**I/O:**

Inputs:
- `data` — string, N inputs to evaluate
- `model` — Model record
- `trigger` — boolean from upstream component

Outputs:
- `route 1..N` — boolean triggers, one per defined output path, only the selected path fires
- `selected` — string, identifier of the chosen route for logging.

---
