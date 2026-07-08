# PhySchema Requirements for GhJSON Translation

> **Scope:** this format applies ONLY to the **SchemaTranslator** component's input. It is NOT the
> LLM pipeline's schema — that is `Files/SYSTEM_PROMPTS/SCHEMA/Node Graph.json`, where connection
> endpoints are matched by `paramIndex` (with `paramName` optional) and `instanceGuid` is omitted
> on added components. Do not use this document to author LLM system prompts.

A JSON document passed to **SchemaTranslator** must conform to the PhySchema format. SchemaTranslator adds canvas positions and outputs a GhJSON string ready for the Deserializer.

---

## Required Top-Level Structure

```json
{
  "schema": "1.0",
  "components": [],
  "connections": []
}
```

All three fields are required. No additional top-level fields are allowed.

---

## Components

Each component object requires three fields:

| Field | Type | Description |
|---|---|---|
| `name` | string | Grasshopper component display name, e.g. `"Sphere"`, `"Addition"` |
| `instanceGuid` | string | UUID v4, unique within the document |
| `id` | integer | Unique positive integer; referenced by connections |

### Optional: componentState

Use only for **Number Slider** to set its value and range:

```json
{
  "name": "Number Slider",
  "instanceGuid": "11111111-1111-1111-1111-111111111111",
  "id": 1,
  "componentState": {
    "extensions": {
      "gh.numberslider": {
        "value": "5<0~10>"
      }
    }
  }
}
```

The value format is `"default<min~max>"`.

---

## Connections

Each connection object represents **one wire** between one output parameter and one input parameter.

```json
{
  "from": { "id": 1, "paramName": "Number" },
  "to":   { "id": 2, "paramName": "X" }
}
```

| Field | Description |
|---|---|
| `id` | Integer id of the source or target component |
| `paramName` | Exact Grasshopper parameter name — case-sensitive |

> **A component with N wired inputs requires N separate connection entries.** For example, a `Construct Point` receiving X, Y, and Z values needs three entries, one targeting each of `"X"`, `"Y"`, and `"Z"`. Omitting any input leaves it at its default (an unwired `Construct Point` always outputs world origin).

---

## Validation (Optional)

SchemaTranslator accepts a JSON Schema string on its **Schema In** (`S`) input. When provided, the document is validated before translation and routed to the failure output on error. When empty, translation proceeds without validation.
