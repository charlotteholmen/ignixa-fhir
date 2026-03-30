# Agent Journal

Set up and activate [AgentJournal](https://www.nuget.org/packages/AgentJournal) as the agent's persistent memory, knowledge retrieval, and learning system.

AgentJournal extends what the agent can know beyond the current context window by indexing:
- **Past agent sessions** (Claude Code, GitHub Copilot CLI) — searchable conversation history
- **Project documentation** (markdown dirs) — indexed and semantically searchable
- **Explicit knowledge entries** — facts, decisions, patterns the agent has learned and stored with temporal decay

---

## Setup

### Install

```bash
agent-journal --version 2>/dev/null || dotnet tool install --global AgentJournal
```

### MCP Server (preferred — enables inline tool use during sessions)

Add to `.mcp.json` in the project root or `~/.claude/mcp_servers.json`:

```json
{
  "mcpServers": {
    "agent-journal": {
      "command": "agent-journal",
      "args": ["mcp"]
    }
  }
}
```

Restart Claude Code. When MCP is active, the agent uses the 14 MCP tools directly — no CLI needed.

### Index Project Documentation

Run once (and re-run when docs change significantly):

```bash
agent-journal content index ./docs --recursive --project <project-name>
agent-journal content index ./README.md --project <project-name>
```

This makes architecture decisions, API docs, and design notes available for semantic search during any future session.

### Index Past Sessions

```bash
agent-journal index                            # all agents
agent-journal index --agent claude-code        # Claude Code only
agent-journal index --watch                    # continuous monitoring
```

---

## How the Agent Uses This

AgentJournal is the agent's long-term memory. These are the behaviors the agent should follow:

### On Session Start — Retrieve Context Before Acting

Before starting any non-trivial task, search across all knowledge sources to surface relevant prior work:

**Via MCP (preferred when active):**
Use the `Search` MCP tool with the task description as the query. It searches sessions, content, and knowledge entries together.

**Via CLI:**
```bash
# Hybrid search (lexical + semantic) across all indexed data
agent-journal search "<task description or keywords>" --mode hybrid --context 3

# Search only indexed documentation
agent-journal content search "<topic>"

# Recall relevant stored knowledge
agent-journal knowledge recall "<topic>"
```

Surface relevant results and incorporate them into your understanding before taking action. Prior sessions may contain: decisions already made, patterns established, mistakes to avoid, architecture choices, or completed prior work.

### During Work — Capture Insights as They Emerge

Don't wait until the end. When you discover something important — a pattern, a constraint, a fix — store it immediately:

**Via MCP:** Use the `Remember` tool.

**Via CLI:**
```bash
agent-journal knowledge remember "<insight>" --project <project-name>
```

Examples of what to remember:
- "The auth service requires X-Tenant-Id on all requests — missing it returns 401 not 403"
- "SearchParameter parsing uses a FrozenDictionary; do not use in EF Core .Contains() — causes runtime error"
- "Integration tests require the TestServer to be started with UseEnvironment('Testing')"

### When Encountering Unfamiliar Code or Patterns

Before guessing, retrieve context:

```bash
agent-journal search "<class name or pattern>" --mode hybrid
agent-journal content search "<topic>"
```

If MCP is active, use `SearchSessions` or `SearchContent` inline.

### On Session End — Consolidate Learning

After completing work, index the session and reinforce knowledge that was relied upon:

```bash
# Index the session that just completed
agent-journal index --agent claude-code

# Reinforce knowledge entries you used (resets their decay timer)
agent-journal knowledge reinforce <id>

# Store any new insights not yet captured
agent-journal knowledge remember "<what was learned>"
```

### Reinforcing Knowledge (Preventing Decay)

Knowledge entries decay with a 90-day half-life by default. If you retrieve a piece of knowledge and rely on it, reinforce it so it remains highly weighted in future searches:

**Via MCP:** Use the `Reinforce` tool.
**Via CLI:** `agent-journal knowledge reinforce <id>`

---

## Reference

### Search Options

```bash
agent-journal search "<query>" --mode hybrid    # lexical + semantic (best quality)
agent-journal search "<query>" --mode semantic  # vector similarity only
agent-journal search "<query>" --mode lexical   # exact/keyword match only
agent-journal search "<query>" --context 5      # include 5 messages of surrounding context
agent-journal search "<query>" -p <project>     # filter to a project
```

### Content (Documentation) Commands

```bash
agent-journal content index <path> --recursive --project <name>  # index a directory
agent-journal content search "<query>"                            # search indexed docs
agent-journal content list                                        # list indexed sources
agent-journal content add --source "<id>" --title "<t>" --content "<text>"  # add inline
agent-journal content remove <id>                                 # remove a source
agent-journal content reinforce <id>                              # reinforce a doc entry
```

### Knowledge Commands

```bash
agent-journal knowledge remember "<fact>" --project <name>
agent-journal knowledge recall "<topic>"
agent-journal knowledge reinforce <id>
agent-journal knowledge forget <id>
```

### MCP Tools (14 available when server is active)

| Category | Tools |
|----------|-------|
| Sessions | SearchSessions, GetSession, ListRecentSessions |
| Knowledge | Remember, Recall, Reinforce, Forget |
| Content | IndexContent, AddContent, SearchContent, ListContent, RemoveContent, ReinforceContent |
| Unified | Search (sessions + content + knowledge combined) |

### Config

```bash
agent-journal config                        # view settings
agent-journal config set halfLife 30        # decay half-life in days (default: 90)
```
