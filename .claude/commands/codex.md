---
description: Run Codex CLI for complex reasoning, alternative perspectives, or web research
allowed-tools: Bash(codex:*)
argument-hint: <prompt> [--search]
---

Run the Codex CLI with the specified prompt.

Execute the following command:

```bash
codex exec -c model="gpt-5.1-codex-max" -c model_reasoning_effort="high" --dangerously-bypass-approvals-and-sandbox $ARGUMENTS
```

## Examples

Basic usage:
```
/codex "analyze this algorithm for optimization opportunities"
```

With web search:
```
/codex --search "latest async/await patterns in JavaScript"
```

Override model (defaults to gpt-5.1-codex-max with high reasoning):
```
/codex -c model="o3" "design a lock-free data structure"
```

## When to Use

- Complex algorithmic analysis requiring deep reasoning
- Getting alternative implementation approaches
- Research tasks with web search (`--search`)
- Tasks benefiting from extended thinking
