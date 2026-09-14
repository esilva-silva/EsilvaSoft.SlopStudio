# EsilvaSoft.SlopStudio

A desktop IDE for MongoDB built with **.NET 10 and Avalonia**, for **Windows and Linux**, with local **LiteDB** storage and an optional **local AI** assistant. Licensed under **MIT**.

> [!WARNING]
> **This is an experimental test project, built entirely with vibe coding.**
>
> All code, tests and documentation in this repository were produced through AI-assisted "vibe coding". It has not gone through a full human code review, security audit or production hardening. Expect bugs, rough edges and design decisions that may change.
>
> - Do **not** point it at production databases without backups and read-only credentials.
> - Treat every write, delete, import and AI-generated proposal as untrusted until you review it.
> - Connection profiles and environment variables are stored **unencrypted** in a local LiteDB file.
>
> **Version 1.0.0 will be fully reviewed** — code, architecture, security, tests and documentation — before it is declared stable. Until then, every release is a pre-release.

## MVP goal (v0.5.0)

Deliver the basic daily MongoDB cycle **without requiring AI or cross-server automation**:

```text
connect → navigate → query → view → edit → export
```

Concretely: save and test connection profiles, browse databases and collections, write and run `find`/`findOne` queries with filter, sort, limit and skip, view results as JSON or a tree while preserving BSON types (ObjectId, UUID, dates), insert/update/delete with confirmation, get deterministic autocomplete, format JSON/queries/scripts, and export the loaded page to Extended JSON or CSV.

## Project phases

| Phase | Version | Goal | Progress |
| --- | --- | --- | --- |
| 1 | v0.5.0 | **MVP**: connect → navigate → query → view → edit → export | ✅ Feature scope implemented (Explorer, queries, protected CRUD, JSON/tree results, autocomplete, formatting, JSON/CSV export). 🚧 Windows/Linux acceptance testing still open |
| 2 | v0.6.0 | Advanced queries and aggregation pipelines | 🚧 In development — pipelines, syntax highlighting, statement execution and contextual autocomplete already exist |
| 3 | v0.7.0 | Administration and maintenance | 🚧 In development — collections, views, validation, indexes, stats, users/roles and logical export/import already exist |
| 4 | v0.8.0 | JavaScript script engine across connections | 🚧 In development — JavaScript Console with `getConnection()`/`ConnectionPool` and a separate `mongosh` Script mode |
| 5 | v0.9.0 | Local AI and contextual productivity | 🧪 Experimental — ONNX models, ghost-text completion, reviewable chat proposals, multi-model catalog with CPU/GPU/NPU selection |
| 6 | v1.0.0 | Stability, full review, installation and updates | 📋 Planned — the **fully reviewed** stable release |

No phase has passed its formal acceptance gate yet: automated tests (including headless UI rendering) do not replace validation against real MongoDB servers, both operating systems, screen readers and native dialogs. Features from later phases that already exist are early previews. Details: [roadmap](docs/09-plano-de-implementacao.md) and [implementation inventory](docs/24-inventario-roadmap.md) (Portuguese).

## Requirements

- **.NET SDK 10.0.400** or a later feature band (pinned in [global.json](global.json)).
- **Windows** (x64/ARM64) or **Linux** (x64/ARM64).
- A reachable **MongoDB** server.
- Optional: **mongosh** on `PATH` for the Script mode.
- Optional: an external **ONNX Runtime GenAI** model export for local AI. Model weights are never downloaded or bundled.

## Getting started

```bash
git clone <repository-url>
cd EsilvaSoft.SlopStudio
dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode
dotnet run --project src/EsilvaSoft.SlopStudio.Desktop
```

## How to use

### Connect and navigate

1. Click **Conexões** (Connections) → **Nova conexão**. Paste a URI such as `mongodb://user:password@server/database` and use **Preencher** to fill the fields from it. Save the profile.
2. **Testar conexão** checks the profile; **Abrir conexão** loads its databases into the Explorer.
3. Expand a database to load its collections. Double-click (or press Enter on) a collection to open a Console tab with a prepared query. **Nothing runs until you execute it.**

Credentials can be written directly in the URI or referenced from a local environment with `${ENV.get("MONGO_PASSWORD")}`. Manage environments under **Ambientes** (Development, Staging, Production or custom).

### Query and edit

Queries are written as text in the editor (there is no form-based query builder):

```javascript
db.getCollection("customers").find({ status: "active" }).sort({ name: 1 }).limit(100)
db.Customers.find({ CustomerId: UUID("00112233-4455-6677-8899-aabbccddeeff") })
getConnection("Development").getDatabase("CRM").getCollection("Customers").findOne({})
```

- Results appear as JSON or a tree. Right-click a document to view, copy or edit it; saving re-reads the document and detects conflicts before writing.
- Writes and destructive operations require confirmation; read-only profiles block them.
- Use **Formatar JSON/query/script** in the editor options to format the selection or the whole tab (undoable).
- **Exportar página…** writes the loaded result page to Extended JSON or CSV.

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `F5` | Run the whole tab |
| `Ctrl+Enter` | Run the selection or the statement at the cursor |
| `Ctrl+Space` | Show suggestions |
| `Tab` / `Esc` | Accept part of an inline suggestion / dismiss it |
| `Ctrl+T` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | New tab / open file / save file / save as |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Switch tabs |
| `Ctrl+W` | Close tab |

### Local AI (optional, experimental)

1. Put each model export in its own folder inside a models directory (default `%LOCALAPPDATA%\EsilvaSoft\SlopStudio\Models` on Windows, `$XDG_DATA_HOME/EsilvaSoft/SlopStudio/Models` on Linux). An optional `slopstudio-model.json` can declare a display name, capabilities and supported hardware.
2. Open **… → Preferências → Autocomplete…**, choose the models directory, click **Atualizar** and pick a model.
3. Choose hardware: **Automático** tries NPU → GPU → CPU and falls back automatically; **CPU**, **GPU** or **NPU** use only that backend and report failures instead of silently falling back.
4. Click **Testar modelo** to validate the folder, tokenizer, ONNX session, provider and a real generation, with load time, first-token latency and tokens per second.

Without a model, autocomplete keeps working with deterministic suggestions. Nothing is sent to external AI services. AI output is a proposal: always review the diff before applying it. See [local multi-model AI](docs/26-ia-local-multimodelo.md) (Portuguese).

## Commands

### Development

```bash
dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode
dotnet build EsilvaSoft.SlopStudio.slnx --no-restore
dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore
dotnet run --project src/EsilvaSoft.SlopStudio.Desktop
```

In sandboxed environments that block Avalonia's build telemetry, add `-p:UsedAvaloniaProducts=` to the build. It does not disable analyzers or tests.

### ONNX Runtime backends

Each build includes exactly one native ONNX Runtime family. Pass the same value to restore, build, test and publish:

| `-p:SlopOnnxBackend=` | Package | Default on |
| --- | --- | --- |
| `WinML` | ONNX Runtime GenAI WinML (CPU + DirectML) | Windows |
| `Cpu` | ONNX Runtime GenAI (CPU) | Linux |
| `Cuda` | ONNX Runtime GenAI CUDA (NVIDIA) | — (opt-in) |

```bash
dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode -p:SlopOnnxBackend=Cpu
dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:SlopOnnxBackend=Cpu
```

### Tests with real resources

Integration tests that need external models are marked `Explicit` and run only when selected by name:

```bash
SLOP_QWEN_MODEL=/path/to/model dotnet test tests/EsilvaSoft.SlopStudio.UnitTests --no-build --filter "Name~RealModelTestRunsOnTheRequestedHardware"
```

`SLOP_QWEN_MODEL` points to a Qwen-compatible ONNX GenAI export and `SLOP_DEEPSEEK_MODEL` to a DeepSeek-Coder FIM export. On Windows PowerShell, set the variable first with `$env:SLOP_QWEN_MODEL = "..."`.

### Release packages

```bash
./build-release.ps1                                  # version from the latest git tag, or 0.0.0-local
./build-release.ps1 0.5.0
./build-release.ps1 0.5.0 -SkipTests -Rids win-x64,linux-x64
```

On Windows, `build-release.bat` wraps the same script. It produces self-contained single-file packages (`.zip` for Windows, `.tar.gz` for Linux) and `SHA256SUMS.txt` in `artifacts/release/<version>`.

### CI/CD

Everything runs on GitHub-hosted runners; no self-hosted or ARM hardware is required.

- [ci.yml](.github/workflows/ci.yml): on every branch push and pull request, builds and tests on Ubuntu and Windows as two parallel jobs.
- [release.yml](.github/workflows/release.yml): on a `v*` tag, packages four executables in parallel jobs while the CI runs:

  | Package | Runner | ONNX backend |
  | --- | --- | --- |
  | `win-x64`, `win-arm64` | `windows-latest` | WinML (CPU + DirectML) |
  | `linux-x64`, `linux-arm64` | `ubuntu-latest` | CPU |

  The GitHub release (packages plus `SHA256SUMS.txt`) is published only after all tests and packages succeed. Tags with a suffix, such as `v0.5.0-alpha.1`, are marked as pre-releases. **Run workflow** with a version builds the same packages as downloadable artifacts, without creating a release.

ARM64 packages are cross-compiled. 32-bit x86 is not built, because ONNX Runtime GenAI ships no 32-bit native libraries.

## Local data and privacy

- The workspace (profiles, history, drafts, preferences, audit) is stored in `workspace.db` under `%LOCALAPPDATA%\EsilvaSoft\SlopStudio` (Windows) or `$XDG_DATA_HOME/EsilvaSoft/SlopStudio` (Linux).
- Results and resolved credentials are not saved in session snapshots, but tab text is, and it may contain sensitive data you typed.
- There is no native OS credential vault yet.
- No code, query, document or credential is sent to AI services; optional ONNX and GenAI telemetry is disabled.

## Documentation

Everything except this README is written in **Portuguese (pt-BR)**, the language of the application UI. Start with the [documentation index](docs/README.md). Contributors should read [AGENTS.md](AGENTS.md) before changing code.

## License

[MIT](LICENSE) © 2026 EsilvaSoft. Third-party components keep their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
