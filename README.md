# daisi-minion

A standalone local AI coding assistant that runs GGUF models via the Daisi Llogos inference engine. Minions are autonomous workers — they load a model, chat with it, execute tools, and complete tasks without cloud dependencies.

Minions can run solo (interactive TUI or headless CLI) or be spawned by a summoner to work as part of a team.

## Architecture

```
Program.cs
  ├── --cli → CliRunner (headless, plain text to stdout/stderr)
  └── default → MinionEngine (full interactive TUI)

Engine/
  MinionEngine.cs         Main TUI agentic loop
  CliRunner.cs            Headless CLI runner (scripting, CI, goal mode)
  ConversationManager.cs  Chat session, history, file tracking, auto-compaction
  MinionToolFormatter.cs  Tool call formatting (JSON + Qwen XML)
  QwenToolCallParser.cs   Parses Qwen 3.5 native XML-style tool calls
  ToolExecutor.cs         Concurrent tool execution with dependency analysis
  ProjectContext.cs       Working directory, git branch, project detection
  RoleManager.cs          Loads role definitions from ~/.daisi-minion/roles/
  PersonaManager.cs       Loads personality traits from ~/.daisi-minion/personas/
  TokenStreamFixer.cs     Cleans up model output artifacts
  ModelDownloader.cs      Download models from HuggingFace
  HuggingFaceClient.cs    Fetch model profiles and generation configs

Coding/
  IMinionTool.cs          Tool interface
  CodingToolRegistry.cs   Tool registration and dispatch
  Tools/
    FileReadTool.cs       Read files with line numbers
    FileWriteTool.cs      Create or overwrite files
    FileEditTool.cs       Search-and-replace editing
    GrepTool.cs           Regex content search
    GlobTool.cs           File pattern matching
    ShellExecuteTool.cs   Shell command execution with timeout
    GitTool.cs            Git operations

Config/
  MinionConfig.cs         Settings (model, backend, context, temperature, etc.)
  ConfigManager.cs        Load/save ~/.daisi-minion/config.json
  ModelProfile.cs         Per-model generation parameters
  ChatHarness.cs          Per-model prompt format, stop sequences, tool call style
  Harnesses/              Built-in harnesses (chatml, llama3, gemma, phi3, bitnet)

Host/
  DualModeOrchestrator.cs Idle → host mode transitions
  HostModeService.cs      Serve model to ORC network when idle
  ActivityMonitor.cs      Track user activity for idle detection

Tui/
  AnsiRenderer.cs         Terminal output with ANSI formatting
  InputHandler.cs         Keyboard input, hotkeys
  Layout/                 Status bar, command bar, console output
  StartupSpinner.cs       Loading indicators
```

## Two Modes

### TUI Mode (default)

Full interactive terminal UI with status bar, live token streaming, syntax highlighting, and hotkeys.

```
daisi-minion
```

Features:
- Slash commands: `/help`, `/model`, `/role`, `/persona`, `/clear`, `/compact`, `/goal`, `/backend`, `/name`
- Hotkeys: cycle roles, cycle personas, view inference log
- Double-Escape to cancel generation mid-stream
- Live stats: token count, tokens/sec, context usage bar
- Think-tag stripping for Qwen-style `<think>` blocks
- Auto-compaction at 90% context usage (summarizes conversation, re-injects fresh file contents)

### CLI Mode

Headless mode for scripting, CI pipelines, and automated workflows.

```
daisi-minion --cli                              Interactive stdin/stdout chat
daisi-minion --cli --goal "Fix the bug"         Autonomous goal mode (up to N iterations)
daisi-minion --cli --model path/to/model.gguf   Override model
daisi-minion --cli --context 4096               Override context size
daisi-minion --cli --backend cuda               Override backend (cpu/cuda/vulkan/auto)
daisi-minion --cli --max-tokens 2048            Override max generation tokens
daisi-minion --cli --max-iterations 10          Max iterations for goal mode (default 20)
daisi-minion --cli --role coder                 Override active role
daisi-minion --cli --json                       Structured JSON output
```

Goal mode runs autonomously: the minion works toward a goal, uses tools, evaluates progress, and exits when it detects `GOAL_COMPLETE` or hits the iteration limit.

## Roles and Personas

**Roles** define what the minion does — its system prompt and tool focus. Built-in roles are seeded to `~/.daisi-minion/roles/` on first run:

| Role | Focus |
|------|-------|
| chat | General conversation |
| coder | Code writing, debugging, refactoring |
| cto | Technical leadership, architecture |
| ceo | Business strategy, high-level decisions |
| team-lead | Task coordination, code review |
| project-manager | Planning, tracking, communication |
| creative-director | Design thinking, creative direction |
| graphic-designer | Visual design, UI/UX |
| marketing-director | Marketing strategy, content |

**Personas** add personality traits layered on top of any role: witty, sarcastic, dry, humorous, concise, direct, friendly, charming, analytical.

Both are markdown files. Users can edit existing ones or drop new `.md` files into the folders.

## Dual-Mode Hosting

When the user is idle, the minion can offer its loaded model to the ORC network for inference by other consumers. When the user returns, it reclaims the model instantly.

- `ActivityMonitor` tracks user activity
- `DualModeOrchestrator` manages idle/active transitions
- `HostModeService` handles ORC registration and heartbeat

> **Status**: Scaffolded but disabled. The transition between host-mode inference and local coding inference needs further work.

## Configuration

Stored at `~/.daisi-minion/config.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `models_directory` | `C:\GGUFS` | Where to find GGUF model files |
| `active_model` | — | Path to the currently selected model |
| `backend` | `auto` | Inference backend: `cpu`, `cuda`, `vulkan`, `auto` |
| `context_size` | 8192 | Default context window size |
| `max_tokens` | 4096 | Default max generation tokens |
| `thread_count` | CPU count - 2 | Threads for CPU inference |
| `temperature` | 0.7 | Sampling temperature |
| `active_role` | `chat` | Active role |
| `active_persona` | — | Active personality trait |
| `minion_name` | `minion` | Display name |
| `working_directory` | — | Override working directory |
| `idle_timeout_minutes` | 5 | Minutes before entering host mode |

Per-model profiles (context size, temperature, top_k, top_p, repetition penalty) are auto-fetched from HuggingFace on first load and saved alongside the model file.

## Chat Harnesses

Each model gets its own **chat harness** that defines how prompts are formatted. Harnesses are stored as `{model-name}.harness.json` in `~/.daisi-minion/models/`.

Resolution order:
1. **User override** — edit the `.harness.json` file to customize prompt formatting
2. **Built-in** — shipped in the assembly for known architectures (ChatML, Llama3, Gemma, Phi3, BitNet)
3. **Auto-detected** — parsed from the GGUF `tokenizer.chat_template` metadata
4. **Default** — ChatML

A harness controls:

| Field | Description |
|-------|-------------|
| `chat_format` | `"chatml"`, `"llama3"`, `"gemma"`, `"phi3"`, or `"custom"` |
| `supports_system_role` | Whether the model has a native system role |
| `user_prefix` / `user_suffix` | Wrapping for user messages (custom format) |
| `assistant_prefix` / `assistant_suffix` | Wrapping for assistant messages (custom format) |
| `generation_prompt` | Text prepended to start assistant generation |
| `stop_sequences` | End-of-turn strings |
| `tool_call_style` | `"json_tags"`, `"raw_json"`, or `"none"` |
| `tool_instruction` | Custom instruction text for tool calling |

Example — the auto-generated BitNet harness:
```json
{
  "chat_format": "custom",
  "supports_system_role": false,
  "prepend_bos": true,
  "user_prefix": "Human: ",
  "user_suffix": "\n\n",
  "assistant_prefix": "BITNETAssistant: ",
  "generation_prompt": "BITNETAssistant: ",
  "stop_sequences": ["Human:"],
  "tool_call_style": "json_tags"
}
```

To iterate on a model's prompt format, edit its harness file and re-run. The harness is loaded on every conversation reset.

## Dependencies

- **Daisi.Llogos** (+ CPU, CUDA, Vulkan backends) — local GGUF inference engine
- **Daisi.Inference** — model configuration types
- **Daisi.Host.Core** — host-mode ORC integration
- **Daisi.SDK** — shared SDK types

## Integration with Daisinet

daisi-minion is a standalone project. It can operate fully independently, but also integrates with the broader Daisinet ecosystem:

- **ORC** — can route inference requests to idle minions via host mode
- **Marketplace** — distributes skills that extend minion behavior via system prompts
- **DaisiGit** — provides per-account git hosting for versioning minion artifacts
- **Summoners** — external coordinators can spawn minions via CLI mode to work as part of a team

## Experiments

Active experiments exploring the future of minion capabilities:

- [Self-Evolving Minions](docs/experiments/self-evolving-minions.md) — Dynamic compilation, minion type hierarchies, and autonomous self-improvement
