# Experiment: Self-Evolving Minions

**Status**: Design phase
**Inspired by**: [Meta Hyperagents](https://github.com/facebookresearch/Hyperagents) — self-referential self-improving agents that optimize via iterative code modification

## Problem

Today, minions are compiled once and never change. A `daisi-minion` binary is the same whether it's fixing a React bug or writing Terraform. Differentiation happens only at the prompt level — roles and personas swap system prompt text, but the actual code, tools, and agentic loop are identical.

Prompt-level differentiation (skills) is interpreted by the model. It's a suggestion, not a guarantee. A skill that says "always run tests after editing" depends on the model's willingness to follow that instruction. We need **hard-coded differentiation** — actual compiled C# that behaves differently depending on the task.

## Design

### Three Layers of Differentiation

| Layer | Mechanism | Reliability | Example |
|-------|-----------|-------------|---------|
| **Skills** | Prompt markdown | Model-dependent | "When reviewing, check for X..." |
| **Modules** | Compiled C# extensions | Deterministic | A post-processing hook that runs `dotnet test` after every edit |
| **Minion Types** | Different core programs | Fundamentally different | `CodeMinion` vs `TestMinion` vs `ResearchMinion` |

Skills are soft. Modules are hard. Minion types are structural.

### Minion Type Hierarchy

```
Minion (base)
  │
  │  Model loading, inference, conversation management
  │  Hardcoded sandboxed tools (file, shell, git, grep, glob)
  │  Base agentic loop
  │
  ├── CodeMinion
  │     Code-focused loop, build/test tools, git workflow
  │     + ReactModule, SecurityModule, etc.
  │
  ├── TestMinion
  │     Test-run-analyze-fix loop, coverage tools
  │     + IntegrationTestModule, LoadTestModule, etc.
  │
  ├── ResearchMinion
  │     Read-only exploration loop, summarization output
  │     + CodebaseAnalysisModule, DependencyAuditModule, etc.
  │
  └── EvolutionMinion
        Meta-minion that improves other minions (see below)
```

Each type inherits the base runtime but overrides what matters: which tools are registered, what the agentic loop looks like, how completion is detected, what evaluation means.

The existing `MinionEngine` is essentially the first `CodeMinion`. The refactoring extracts `Minion` as the base class.

### Dynamic Modules (Roslyn Compilation)

Modules are `.cs` source files compiled at runtime via Roslyn. A module implements `IMinionModule`:

```csharp
public interface IMinionModule
{
    string Name { get; }
    string Description { get; }

    // Lifecycle
    void Initialize(MinionModuleContext context);

    // Extension points
    string? ExtendSystemPrompt();
    IEnumerable<IMinionTool>? GetTools();
    string? PreProcess(string userInput);
    string? PostProcess(string response);
    Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome);
}
```

Example — a module a local model could write:

```csharp
public class ReactCodeReviewer : IMinionModule
{
    public string Name => "react-reviewer";
    public string Description => "Specialized React code review";

    public void Initialize(MinionModuleContext context) { }

    public string? ExtendSystemPrompt() =>
        "When reviewing React code, check for: missing keys in lists, " +
        "stale closures in useEffect, unnecessary re-renders, " +
        "missing dependency arrays, and prop drilling that should use context.";

    public IEnumerable<IMinionTool>? GetTools() => null;

    public string? PreProcess(string input) => null;
    public string? PostProcess(string response) => null;

    public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
        Task.FromResult(new ModuleEvaluation
        {
            Score = outcome.UserApproved ? 1.0 : 0.3,
            Notes = outcome.UserApproved ? "Review accepted" : "Review rejected or ignored"
        });
}
```

Modules compose. A minion can load multiple:

```
spawn CodeMinion --modules react,security "Fix the XSS vulnerability in the login form"
```

### Sandboxed Tools as the Security Boundary

Dynamically compiled code is dangerous. The sandbox isn't "modules can't do dangerous things" — it's **"modules can only do dangerous things through supervised channels."**

The base `Minion` class provides hardcoded tools that cannot be overridden or removed:

```csharp
public abstract class Minion
{
    // Sealed — modules cannot override, remove, or bypass
    protected sealed FileReadTool FileRead { get; }
    protected sealed FileWriteTool FileWrite { get; }
    protected sealed FileEditTool FileEdit { get; }
    protected sealed ShellExecuteTool Shell { get; }
    protected sealed GitTool Git { get; }
    protected sealed GrepTool Grep { get; }
    protected sealed GlobTool Glob { get; }
}
```

Invariants enforced at the tool level, not the module level:

- **File tools**: Paths resolved and rejected if outside the minion's scoped working directory. No `../../etc/passwd`.
- **Shell tool**: All processes get timeouts, run in the scoped directory, and get process-tree cleanup on cancel or shutdown.
- **Git tool**: Operates only within the sandbox.

Roslyn compile-time gate: modules can only reference module SDK types (`IMinionTool`, `ToolResult`, `JsonObject`, basic collections). Direct use of `System.Diagnostics.Process`, `System.IO.File`, `System.Net`, or `System.Reflection.Emit` is rejected at compilation.

The module decides **what** to do. The host decides **how** it's allowed to happen.

### Isolation Progression

| Phase | Isolation | Trust Level |
|-------|-----------|-------------|
| **Local dev** | Roslyn AST gates + AssemblyLoadContext | You wrote it |
| **Summoner-generated** | + process isolation (restricted Windows token, named pipe comms) | LLM wrote it |
| **Marketplace** | + WASM sandbox (Wasmtime, capability-based) | Strangers wrote it |

## The Evolution Loop

### Actors

**Summoner** — A lazy leader. Knows which minion types and modules exist, their scores, which branches and trials are complete. Picks the right combinations for the job. Delegates all real work. Does not write code or evaluate quality.

**EvolutionMinion** — A specialized minion type whose purpose is improving other minions. It:
- Reads evaluation results from completed tasks
- Analyzes what went wrong or could be better
- Writes improved `.cs` module source files
- Tests the improvements (compiles, runs, evaluates)
- Can evolve itself — its own modules are subject to the same loop
- Commits improved versions to git branches

**Working Minions** (CodeMinion, TestMinion, etc.) — Do the actual work. After each task, they self-evaluate and record outcomes.

### The Cycle

```
1. Summoner assigns task → CodeMinion + modules
2. CodeMinion runs, completes task
3. CodeMinion self-evaluates → ModuleEvaluation { Score, Notes }
4. Evaluation stored alongside module version

    ... after N tasks ...

5. Summoner spawns EvolutionMinion:
   "The react-reviewer module scored 0.3 on the last 5 tasks.
    Here are the evaluation notes. Write an improved version."

6. EvolutionMinion:
   a. Reads current module source
   b. Reads evaluation history
   c. Writes improved .cs file
   d. Compiles it (Roslyn) — if it fails, iterates
   e. Commits to a git branch: modules/react-reviewer/v2
   f. Optionally runs a test task to validate

7. Next time summoner needs react review:
   - Sees v2 branch exists with passing tests
   - Spawns CodeMinion with the v2 module
   - If v2 scores better over time, it becomes the default
   - If worse, summoner falls back to v1
```

### How This Maps to Hyperagents

| Hyperagent Concept | Minion Equivalent |
|---|---|
| Task Agent (stateless Python) | Working Minions (compiled C# types + modules) |
| Meta Agent (modifies task agent code) | EvolutionMinion (writes and tests new module source) |
| `archive.jsonl` (lineage + scores) | Git history + evaluation records |
| `model_patch.diff` | Git commits to module `.cs` files |
| Docker isolation per generation | Git branches per module version |
| Parent selection (best/latest/random) | Summoner picks module version by score + trial status |
| Staged evaluation (cheap then full) | Quick compile test, then real task evaluation |

Key differences from Hyperagents:
- **Compiled, not patched** — modules are type-checked C#, not Python diffs. Bad code fails at compile time.
- **Git, not flat files** — full version history, branches, diffs, PRs, blame.
- **Composable** — multiple modules stack on a minion type. Hyperagents replaces the whole agent.
- **Humans in the loop** — marketplace review workflow already exists for publishing evolved modules.
- **Persistent identity** — minions remember across sessions. Hyperagents task agents are stateless.

## Storage Progression

| Phase | Where Modules Live | Sharing |
|-------|-------------------|---------|
| **Now** | `~/.daisi-minion/modules/` | Local only |
| **DaisiGit** | Per-account git repo | Team/org sharing via DaisiGit permissions |
| **Marketplace** | Published as marketplace items | Cross-account, reviewed, priced |

DaisiGit is the natural home because:
- Already partitioned by account in Cosmos DB
- REST API with bot tools (13 tools: ReadFile, BrowseFiles, CreatePR, etc.)
- Orgs + Teams for permission control
- The EvolutionMinion can use DaisiGit tools to manage branches and PRs

## Open Questions

1. **Module composition conflicts** — What happens when two modules both define `PostProcess`? Chain them? Priority order? Let the summoner decide?

2. **Evaluation quality** — Self-evaluation is only as good as the model doing the evaluating. Should the summoner (or user) provide ground-truth scores?

3. **Evolution speed** — How many evaluation cycles before the EvolutionMinion should attempt an improvement? Too few = noisy signal. Too many = slow adaptation.

4. **Cross-type evolution** — Can the EvolutionMinion propose that a task should use a different minion *type*, not just different modules? ("This task would work better as a TestMinion, not a CodeMinion.")

5. **EvolutionMinion evolving itself** — This is recursive self-improvement. What's the stopping condition? Do we need a "constitution" — invariants that no evolution can violate?

## Next Steps

1. Extract `Minion` base class from `MinionEngine`
2. Define `IMinionModule` interface and `MinionModuleContext`
3. Build `MinionCompiler` (Roslyn compilation with AST safety gates)
4. Build `MinionModuleLoader` (load from `~/.daisi-minion/modules/`, compile, cache)
5. Wire module extension points into the agentic loop
6. Create `CodeMinion` as first concrete type (essentially current `MinionEngine`)
7. Build a simple evaluation recording system
8. Add `--module` flag to CLI mode for summoner integration
