# Implement and Iterate Task

- Respect claude.md
- Use MCP servers to assist
- Delegate to one of our coding agents (@agent-fast-coding-agent [simple tasks], @agent-coding-agent [medium complexity] or @agent-complex-coding-agent [high-complexity]) when possible
- Escalate to @agent-highly-complex-coding-agent (Fable) ONLY when an agent has failed to make progress, or for cross-cutting architecture / deep concurrency or performance bugs - pass it a failure dossier (what was tried, errors, ruled-out hypotheses)
- spawn as many as needed
- Always use modern language syntax when possible.
- When user it happy, run `/fn-accept` to finalize the feature.

## Iteration Loop

1. **Implement** sub-task
2. **Build & Test**
3. **Fix** if needed (repeat 1-2)
4. **Next** sub-task
