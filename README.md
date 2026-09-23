# Snoopy Voice Assistant

A **.NET 10 C# console application** for sequential voice conversations:

```text
Default Windows microphone -> Azure Speech STT -> user's text
    -> explicit memory command -> memory service -> local PostgreSQL
    -> otherwise -> Azure-hosted LLM + bounded history + optional saved-memory context
    -> response text -> Azure Speech TTS -> default Windows output
    -> listen for the next turn
```

Running without arguments starts a conversation. The original working STT,
TTS and echo services/menu are preserved: use **`--speech-tests`** to run them
independently, without Azure OpenAI or database configuration.

V0.2 separates session-history storage from conversation orchestration and adds
voice commands to start a fresh conversation. Speech and model integrations,
speech/model behavior and existing history limits are unchanged.

V0.4 persists explicit memories in **your existing local PostgreSQL instance**
using EF Core. The V0.3.1 remember, forget, recall and clear commands are unchanged.
They do not make an Azure OpenAI request and use the existing TTS pipeline.
Ordinary conversation does not automatically create memories. Opt-in memory-aware
replies can supply a bounded selection of saved memories to the model, allowing
natural questions such as "Do you remember my name?" after a restart. Saved
memories survive restarts; conversation history remains in memory and is lost on exit.

There is no Docker, new PostgreSQL installation, cloud database, Redis, vector
search, embedding, MCP, tool calling, agent framework, web API, device control or
Raspberry Pi integration in this milestone.

## Prerequisites

- Windows and the **.NET 10 SDK**, not .NET 8 or 9. Check `dotnet --version`.
- Working default Windows microphone and audio output. When using a headset,
  select its microphone under Settings -> System -> Sound -> Input.
- Microsoft Visual C++ Redistributable for Visual Studio 2015-2022, matching
  the architecture of .NET, for the native Azure Speech SDK.
- Internet access to NuGet and your Azure services.
- Your already-installed, running local PostgreSQL server and a **dedicated**
  database for Snoopy, normally `snoopy`. Do not reuse another application's database.
- Azure Speech resource **ds-voice**, region **Central India** (`centralindia`).
- An existing Azure OpenAI deployment supporting **v1 Chat Completions**.
  The initial target is **GPT-4.1 (2025-04-14)**. Its deployment name and model
  version are managed in Azure, not hardcoded in Snoopy.
- Local API keys for the respective resources. Usage may incur Azure charges.

Both projects target `net10.0`. `global.json` permits stable .NET 10 feature
bands, but not .NET 11.

## Configuration

### One local appsettings.json for everything

Snoopy loads **only `appsettings.json` in the current working directory**, in both
Debug and Release. User Secrets, `secrets.json`, `.env` and environment-variable
overrides are **not loaded**. This applies to Speech, Azure OpenAI, PostgreSQL and
optional Snoopy settings.

From the **solution root**, create the local file if it does not already exist:

```powershell
Set-Location 'P:\Project snoopy'
if (-not (Test-Path .\appsettings.json)) {
    Copy-Item .\appsettings.example.json .\appsettings.json
}
notepad .\appsettings.json
```

Fill in your settings locally. This example contains placeholders only:

```json
{
  "ConnectionStrings": {
    "SnoopyDatabase": "Host=localhost;Port=5432;Database=snoopy;Username=<local-user>;Password=<local-password>"
  },
  "AzureSpeech": {
    "ApiKey": "YOUR_SPEECH_KEY",
    "Region": "centralindia",
    "VoiceName": "en-IN-NeerjaNeural"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "ApiKey": "YOUR_AZURE_OPENAI_KEY",
    "DeploymentName": "YOUR-DEPLOYMENT-NAME"
  }
}
```

The checked-in [appsettings.example.json](appsettings.example.json) has **empty
keys and connection strings** so startup fails safely until you configure your
local copy. It includes optional timeout, output-token, prompt, history and
memory-context settings. Memory context is disabled by default; enable it only
if you want saved-memory content sent to your configured Azure OpenAI deployment.
Use the Azure **deployment name**, not the model-version date. Speech needs its
key and region; no Speech endpoint URL is required.

Initialize the database as described below, then run Snoopy normally.
Restart after edits because settings are read at startup. Nested JSON and colon
keys such as `"AzureSpeech:ApiKey"` are supported. Missing or malformed files,
blank required values and invalid connection strings produce sanitized errors.

**The local file contains plaintext credentials.** It is ignored by Git and is
not copied to build/publish output. Do not force-add it, share it, capture it in
screenshots, or print it in logs. Restrict its Windows file permissions to the
account running Snoopy. On another machine, create a separate local copy.
Existing User Secrets can remain as an unused backup, but do not affect execution.

### Local PostgreSQL and memory migrations

Use your **existing local PostgreSQL server**. In pgAdmin or your usual local
administration tool, select or create a dedicated `snoopy` database owned by your
local PostgreSQL role. Do not select an existing application database or delete
an existing database/table to make setup work. No server installation, container,
Docker Compose or cloud service is needed or provided.

Connection string format (placeholders only):

```text
Host=localhost;Port=5432;Database=snoopy;Username=<local-user>;Password=<local-password>
```

Store it under **`ConnectionStrings:SnoopyDatabase`** in the ignored local
`appsettings.json`, alongside your Azure settings. Do not put real connection
details into the checked-in example file. There are no environment overrides.
The connection must specify Host, Database and Username; passwords or other
authentication are supplied locally. Sensitive provider error details and
parameter logging are disabled.

Apply the checked-in EF Core migration **explicitly**, from the solution root:

```powershell
dotnet restore .\Snoopy.sln
dotnet build .\Snoopy.sln --no-restore
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --migrate-database
```

This command uses only database configuration: it does not require Azure
credentials, open the microphone, or start the conversation loop. Success prints
`[MEMORY] Memory database schema is up to date.`; failure prints a sanitized
diagnostic and returns a nonzero exit code. Ctrl+C cancels migration work.
Normal voice startup **never applies migrations** and never falls back to
volatile memory when database configuration is missing.

The initial migration is `20260923195326_InitialCreate`. Applying it creates the
`Memories` table and EF's `__EFMigrationsHistory` metadata table. Applying it again
does not recreate or clear data. The local role needs schema-creation permissions
for initialization; if the database does not exist, EF also needs permission to
create it. Prefer preparing the dedicated database through your local admin tool.
There is no `EnsureDeleted`, `EnsureCreated`, automatic reset, or startup deletion.

For development migration tooling, [dotnet-tools.json](dotnet-tools.json) pins
`dotnet-ef` **10.0.12**. These commands do not apply database changes:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list --no-connect --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj
dotnet tool run dotnet-ef migrations has-pending-model-changes --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj
dotnet tool run dotnet-ef migrations script --idempotent --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj
```

The design-time factory loads the same database settings without running the voice
application. For future deployments, review generated forward migration SQL and
apply it as a separate, approved maintenance/deployment step using a migration
identity with schema permissions and an appropriate backup. Runtime identities
need only the data permissions used by Snoopy; do not add automatic production
schema changes or run down migrations against saved memories.

### Speech and model settings

| JSON setting | Meaning |
| --- | --- |
| `AzureSpeech:ApiKey` | Required Speech resource key |
| `AzureSpeech:Region` | Required Speech region; `centralindia` for ds-voice |
| `AzureSpeech:VoiceName` | Optional; defaults to `en-IN-NeerjaNeural` |
| `AzureOpenAI:Endpoint` | Required in conversation mode; Azure resource root or its `/openai/v1/` URL |
| `AzureOpenAI:ApiKey` | Required in conversation mode; key for that Azure OpenAI resource |
| `AzureOpenAI:DeploymentName` | Required in conversation mode; **deployment name**, which need not equal the model name |
| `ConnectionStrings:SnoopyDatabase` | Required for normal conversation and migration modes, but not `--speech-tests` |

Speech still uses a **key + region**, not an endpoint URL, and recognition is
**English (India), `en-IN`**. The voice setting changes TTS only. Programmatic
Speech options retain the `centralindia` default, but startup requires a region
provided explicitly in JSON.

For the LLM, both `https://RESOURCE.openai.azure.com/` and
`https://RESOURCE.services.ai.azure.com/` are supported. The adapter normalizes
either to `/openai/v1/`. Do not use a deployment-specific `/chat/completions`
request URL, query string, dated `api-version`, or a Foundry project URL.
HTTPS and the two Azure resource hostname suffixes are enforced to avoid
accidentally sending a key to a non-Azure host. Custom gateways and sovereign
cloud endpoints are not supported by this initial validator.

### Optional tuning and model changes

| JSON setting | Default |
| --- | --- |
| `AzureOpenAI:RequestTimeoutSeconds` | `45` |
| `AzureOpenAI:MaxOutputTokens` | `1024` |
| `AzureOpenAI:InstructionRole` | `System` |
| `Snoopy:SystemPrompt` | Dedicated voice-friendly prompt |
| `Snoopy:MaxHistoryTurns` | `12` completed user/assistant pairs |
| `Snoopy:MaxHistoryCharacters` | `24000` |
| `Snoopy:MaxInputCharacters` | `4000` |
| `Snoopy:EnableMemoryContext` | `false` (opt in to memory-aware AI replies) |
| `Snoopy:MaxMemoryContextItems` | `20` (allowed: 1-100) |
| `Snoopy:MaxMemoryContextCharacters` | `4000` (allowed: 512-32000) |

When memory context is enabled, its full configured character allowance must fit
alongside `SystemPrompt` and `MaxInputCharacters` within `MaxHistoryCharacters`.
Invalid limits fail configuration validation rather than silently exceeding the budget.

To change models, update `AzureOpenAI:DeploymentName` in `appsettings.json` and restart.
Change endpoint/key too if switching resources. Any replacement must
support Azure v1 Chat Completions and the configured parameters. The API's
`model` field receives the **deployment name**, not a hardcoded GPT version.
For models requiring developer instructions, set the instruction role to
`Developer` (this optional message type is still marked experimental by the
official SDK; the default `System` path is not). Temperature is deliberately not forced. Reasoning models may
need a larger output-token budget, which can include reasoning tokens.
If a future model requires a different API, only the isolated language-model
adapter needs to change, not Speech or the voice loop.

## Restore, build, test and run

```powershell
Set-Location 'P:\Project snoopy'
dotnet --version
dotnet restore .\Snoopy.sln
dotnet build .\Snoopy.sln --no-restore
dotnet test .\tests\Snoopy.Voice.Console.Tests\Snoopy.Voice.Console.Tests.csproj --no-build

# Continuous voice conversation:
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build

# Explicit database initialization/update (database configuration only):
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --migrate-database

# Original independent Speech test menu (no LLM or database configuration needed):
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --speech-tests

# Configuration/usage help without opening devices or contacting Azure:
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --help
```

After editing code, rebuild or omit `--no-build` when running. VS Code includes
`build Snoopy`, `run Snoopy`, and `run Snoopy Speech tests` tasks; run tasks depend
on the build task. Tasks use the solution root as their working directory.
All tasks use the same ignored `appsettings.json`; no terminal-specific
environment configuration or User Secrets are needed.

Startup reports **configured**, not **connected**: validation and SDK construction
do not prove connectivity. Actual calls verify access when used. No real key,
raw HTTP response/header, SDK exception details or stack trace is printed.

### Run from Visual Studio 2026

1. Open `Snoopy.sln` and set **Snoopy.Voice.Console** as the startup project.
2. In the dropdown beside the green Start button, choose
   **Snoopy - Migrate Database**.
3. Press **Ctrl+F5** (Start Without Debugging), or **F5** to debug.
4. Wait for `[MEMORY] Memory database schema is up to date.`
5. Switch back to **Snoopy** before starting normal voice conversation.
   **Snoopy - Speech Tests** opens the independent STT/TTS/echo menu.

Expand **Solution Items** in Solution Explorer to open your local `appsettings.json`
or the checked-in `appsettings.example.json`. These entries reference the files in
the solution root; they do not copy them into a project or build output. The local
file remains Git-ignored even though it is visible in Visual Studio. If Solution
Items does not appear after an external solution edit, accept Visual Studio's
reload prompt or close and reopen `Snoopy.sln`.

The profiles in
[launchSettings.json](src/Snoopy.Voice.Console/Properties/launchSettings.json)
set the working directory to the solution root, so Visual Studio reads your
existing ignored `appsettings.json` without copying credentials into build output.
If the profiles do not appear immediately, reload the project. Selecting or
building the migration profile does not change the database; running it explicitly
applies pending migrations.

## Conversation behavior

1. Snoopy listens for a final, non-empty STT result. Partial results and silence
   are never sent to the LLM. No-match results print a brief retry hint.
2. It stops/disposes the microphone iterator **before** LLM or TTS work.
3. The console shows `You: ...`. Exit, conversation reset and explicit memory
   commands are handled locally. For other input, the conversation service sends
   the system prompt, retained history and current user message to Azure.
4. The console shows `Snoopy: ...` before playing the response.
5. Playback completes before the next microphone session begins. This prevents
   Snoopy from transcribing its own speaker output.

Say **exit**, **quit**, **goodbye** or **stop**, optionally addressed to Snoopy,
to hear "Goodbye! Talk to you later." and exit without an LLM call. Comparisons
ignore case and recognition punctuation. Ordinary sentences containing one of
these words do not exit. **Ctrl+C** cancels STT, an LLM call or TTS and shuts down.
There is no simultaneous listening/barge-in during LLM processing or playback.

Say **reset conversation** or **new conversation** to clear this session's history.
Snoopy replies, "Sure. I've started a new conversation.", then listens again.
Neither the command nor its confirmation is sent to the LLM or stored in history.
Matching ignores case and recognition punctuation, but only these complete
commands trigger a reset; a question containing the words does not.
Reset does not exit, change configuration, modify the system prompt or clear
saved memories.

Known STT failures retry after a one-second delay (Ctrl+C always exits). The
official model SDK makes bounded transient retries, within a total configured
request deadline. A failed model turn displays a sanitized diagnostic and speaks
a fallback, then resumes listening. Malformed/empty or token-truncated model
responses are treated as failures rather than successful conversation turns.
TTS failure leaves the answer visible and resumes listening; farewell playback
failure still exits. No raw provider diagnostics are exposed.

### In-memory history

Each session starts fresh. History contains complete user/assistant pairs,
only committed after a successful model response. Failed/canceled model calls
and standalone spoken error fallbacks are not stored. A valid model reply generated
without available memory context retains its explicit warning in history. A
displayed answer remains in history even if its audio playback fails.

`ConversationService` depends on `IConversationHistory` for reading snapshots,
adding complete turns and clearing history. `InMemoryConversationHistory` owns
the stored turns and limits, and DI registers one instance per application/session.
It is not static, shared across separate application instances or persisted.
Request snapshots reserve room for pending input and any memory-context messages
without modifying stored history.
`ReplyAsync` and `ClearAsync` use the same existing session semaphore, so a reset
waits for an in-flight turn instead of allowing its reply to repopulate cleared
history. Individual in-memory history operations are also synchronized.

The oldest **whole pairs** are dropped to meet both the turn-count cap and the
character budget. The budget includes the system prompt, pending input and any
memory-context instructions/data for each request; it also bounds stored history
including the base system prompt. Memory context is not stored as conversation
turns, so a request may use fewer pairs than remain in session storage.
An individually oversized pair is dropped rather than retained as a half-turn.
This is a character-based bound, **not an exact tokenizer**. Choose limits that
fit the replacement model's context window, with room for output and protocol
overhead. Older conversation facts can be forgotten after truncation. There is no
history summarizer or history persistence; explicitly saved memories are separate.

The app does not write conversations to disk and requests no stored chat
completion (`store: false`). Audio/text still go to the configured Azure services
for processing and are subject to those services' data-handling policies.

### Memory-aware AI replies (opt-in)

Set `Snoopy:EnableMemoryContext` to `true` in your local `appsettings.json` and
restart Snoopy. Ordinary questions then load a fresh PostgreSQL memory snapshot
before each model request. This is separate from the exact local recall commands:
"Do you remember my name?" goes to the model with saved facts, while "What do you
remember about me?" still lists memories directly without a model call.

[MemoryContextProvider](src/Snoopy.Voice.Console/AI/MemoryContextProvider.cs) uses
the existing `IMemoryService`; there is no schema change or new infrastructure.
Selection ranks distinct keyword matches to the current question first, ignoring
common words, then newer `UpdatedAt`, higher importance and ID. Recent memories
fill remaining space when keywords do not match. This is simple lexical ranking,
not semantic/vector search. An older saved name can therefore outrank unrelated
recent memories when the question contains "name".

By default, at most **20 memories and 4,000 added characters** are sent per reply.
The character limit includes the fixed instructions, document delimiters and
JSON-escaped data, not just memory content. Complete facts that do not fit are
skipped, never cut into partial facts. Normalized duplicate facts are included
once without deleting duplicates from PostgreSQL. Explicit recall still lists
every saved memory and is useful when a fact is outside the selected context.

Saved text is JSON-escaped inside a delimited, user-role document, separate from
the fixed system/developer instructions. The model is instructed to treat it as
untrusted reference data, not executable instructions; use relevant facts; avoid
inventing memories; and prefer newer timestamps or ask when facts conflict.
These boundaries reduce prompt-injection risk but do not guarantee model behavior.
The model cannot save, update or delete memories. Only explicit local commands can.

**Privacy:** enabling this feature sends the selected saved facts to your configured
Azure OpenAI deployment on ordinary conversation turns, even after a restart.
Leave it disabled to keep saved-memory content out of model requests. Explicit
recall responses still use Azure Speech TTS, as before.

If PostgreSQL cannot be read, Snoopy logs a sanitized error, continues normal chat,
and prefixes the spoken/displayed reply with "I couldn't read saved memories for
this reply." The model receives an unavailable status, not a misleading empty
memory list. Caller cancellation still stops the operation.

Remember/forget/clear changes are reflected in the next context snapshot.
Previously spoken facts may still occur in this session's ordinary chat history;
forgetting a saved fact does not erase those prior turns. Use "reset conversation"
or restart to clear chat history as well. Resetting chat history alone never
removes saved memories.

### Explicit memory management

The voice application routes commands before calling the conversation service:

```text
VoiceConversationApplication
  -> IMemoryCommandParser -> MemoryCommandResult
       Remember / Forget / Recall / Clear
         -> IMemoryService -> IMemoryStore -> PostgreSQLMemoryStore
              -> SnoopyDbContext -> local PostgreSQL
       None
         -> ConversationService -> IConversationHistory -> InMemoryConversationHistory
                                -> IMemoryContextProvider -> IMemoryService (when enabled)
  -> existing display and TTS pipeline
```

[MemoryCommandParser](src/Snoopy.Voice.Console/Memories/MemoryCommandParser.cs)
only recognizes commands and extracts content. It does not access storage or
Azure. The result contains `CommandType` and `Content`; complete recall/clear
commands are checked before the generic `forget` prefix.

| Say | Action |
| --- | --- |
| `Remember that I prefer .NET for backend development.` | Save `I prefer .NET for backend development.` and speak a confirmation. |
| `Remember I prefer .NET for backend development.` | The word `that` is optional. |
| `Please remember that I like C#.` / `Please remember I like C#.` | The leading `please` is optional. |
| `Forget that I prefer .NET for backend development.` | Remove normalized full-text matches, or say no matching memory was found. |
| `Forget I prefer .NET for backend development.` | `that` and leading `please` are also optional for forgetting. |
| `What do you remember about me?` / `What do you remember?` | Read the saved memories, or say there are none. |
| `Forget everything you remember.` / `Clear my memories.` | Clear saved memories only, not conversation history. |

Commands ignore case and surrounding whitespace. Recall and clear also ignore
extra whitespace and trailing sentence punctuation. Ordinary sentences such as
`I prefer .NET for backend development.` still go to the model and **do not**
create a saved memory. Commands and their local responses are not stored as
conversation turns or sent directly to Azure OpenAI. With memory context enabled,
the saved content may be supplied on later ordinary model turns. Memory responses
still pass through Azure Speech TTS, just like ordinary replies.

An incomplete `remember` or `forget` command gets a spoken request for content,
not an empty memory, a model call or a success confirmation. Responses quote the
original memory wording rather than attempting grammatical/pronoun rewriting:

```text
You: Remember that I prefer .NET for backend development.
Snoopy: Got it. I've saved this memory: "I prefer .NET for backend development."
You: What do you remember about me?
Snoopy: Here's what I remember: "I prefer .NET for backend development."
You: Forget that I prefer .NET for backend development.
Snoopy: Okay, I've forgotten that.
```

[MemoryService](src/Snoopy.Voice.Console/Memories/MemoryService.cs) depends only
on `IMemoryStore`. Its business operations are:

- `RememberAsync`: validate content, trim its outer whitespace, assign a category,
  create a `Memory`, store it and return it. Internal wording/punctuation is kept;
  importance remains zero and the existing model supplies identity/UTC timestamps.
- `ForgetAsync`: compare the complete content using ordinal case-insensitive
  matching, collapsed whitespace and ignored trailing `.`, `!`, `?`. Remove every
  matching ID and return whether anything was actually removed. Repeated saves
  remain separate memories, but forgetting their content removes all exact
  normalized duplicates.
- `GetMemoriesAsync`: retrieve the current store snapshot. The application reads
  all entries in creation-time order (ID breaks ties), without model summarization.
- `ClearMemoriesAsync`: clear only the memory store. Repeated clears are safe.

**Forgetting is not a partial or fuzzy search.** If the saved content is
`My favorite programming language is C#.`, saying `Forget my favorite programming
language` reports no match. Use `Forget that my favorite programming language is
C#.` instead. Internal punctuation and symbols are preserved: `.NET` is not `NET`,
and `C`, `C#`, `C++` and `C sharp` are distinct. Check the displayed recognition
text when speech recognition uses different wording.

Category detection uses these case-insensitive whole words/phrases, not an AI
classifier:

| Content contains | Category |
| --- | --- |
| `prefer` or `preference` | Preference |
| `goal` or `want to` | Goal |
| `my wife`, `my son` or `my family` | Person |
| Matches more than one category above | Other |
| None of those rules | Fact |

`IMemoryCommandParser -> MemoryCommandParser` and `IMemoryService -> MemoryService`
remain singletons with no model, Speech, console, audio or EF dependencies.
Real conversation execution registers exactly one `IMemoryStore`, backed by
PostgreSQL. Unit tests explicitly supply `new InMemoryMemoryStore()` to the
composition root; no environment switch silently changes production storage.
Speech-test mode needs neither memory services nor database configuration.

`[MEMORY]` messages report operation progress without credentials or connection
details. Expected database failures become a sanitized `MemoryStorageException`;
the voice application logs the safe diagnostic, speaks "Memory storage is
currently unavailable. Please try again later.", and continues listening.
It does not pretend that a failed recall is an empty list or that a failed write
succeeded. Normal conversation remains usable during a database outage.
Cancellation still propagates. TTS failure does not roll back a completed memory
operation; the displayed reply remains. Writes are not automatically retried
after an uncertain connection failure.

`reset conversation` / `new conversation` clear only conversation history.
`forget everything you remember` / `clear my memories` clear only saved memories.
Neither action clears the other store. No automatic extraction, embeddings or
semantic/vector search is included; bounded memory context is opt-in as described above.

#### PostgreSQL storage and schema

The existing domain model and storage contracts are unchanged. The separate
[MemoryEntity](src/Snoopy.Voice.Console/Persistence/MemoryEntity.cs) keeps EF mapping
out of the immutable domain record.

| Column | PostgreSQL type | Meaning |
| --- | --- | --- |
| `Id` | `uuid`, primary key | Domain-generated identity; duplicate IDs are rejected. |
| `Content` | `text NOT NULL` | Exact supplied content. |
| `Category` | `text NOT NULL` | Enum name, so enum ordering does not change stored meaning. |
| `Importance` | `integer NOT NULL` | Unchanged signed 32-bit value. |
| `CreatedAt` | `timestamp with time zone NOT NULL` | Creation instant in UTC. |
| `UpdatedAt` | `timestamp with time zone NOT NULL` | Update instant in UTC. |

Only the primary-key index is created: current operations use ID lookup/deletion
or a full snapshot. There are no vector columns, conversation-history tables or
unneeded indexes. PostgreSQL timestamps have **microsecond** precision; .NET's
sub-microsecond ticks cannot be retained by `timestamp with time zone`.

[PostgreSQLMemoryStore](src/Snoopy.Voice.Console/Persistence/PostgreSQLMemoryStore.cs)
uses `IDbContextFactory<SnoopyDbContext>` to create and asynchronously dispose a
fresh context per operation. The singleton store never holds a shared live context.
Reads are no-tracking snapshots of immutable domain records. Delete/clear use
single SQL statements against `Memories`, without loading tracked entities or
affecting conversation history. Neither the memory service nor parser references
EF Core. Invalid stored data also produces a sanitized read failure.

All application instances configured for the same database see the same memory
collection. This milestone has no user identities or per-user database partition;
use a dedicated database per independent Snoopy memory collection.

[Memory](src/Snoopy.Voice.Console/Memories/Memory.cs) is an immutable record:

| Property | Type and behavior |
| --- | --- |
| `Id` | A generated `Guid`, or an explicitly supplied non-empty `Guid`. |
| `Content` | A non-blank `string`, preserved exactly without trimming. |
| `Category` | `MemoryCategory`: Fact, Preference, Person, Goal, Routine, Event or Other (default). Undefined enum values are rejected. |
| `Importance` | An `int`, defaulting to zero and stored verbatim. No scoring scale or range constraint is imposed; it breaks ties after keyword relevance and recency in optional model context. |
| `CreatedAt` | A `DateTimeOffset`, defaulting to the current UTC time. Supplied offsets are normalized to UTC without changing the instant. |
| `UpdatedAt` | A UTC `DateTimeOffset`, defaulting to the creation instant. A supplied update cannot precede creation. |

[IMemoryStore](src/Snoopy.Voice.Console/Memories/IMemoryStore.cs) exposes only:

- `AddAsync(Memory, CancellationToken)`: add a memory; duplicate IDs throw
  `InvalidOperationException` rather than overwriting. Matching content with
  different IDs is allowed.
- `GetByIdAsync(Guid, CancellationToken)`: return the memory, or `null` if absent.
- `GetAllAsync(CancellationToken)`: return a read-only snapshot, with no ordering
  guarantee. Later additions, removals and clears do not change existing snapshots.
- `RemoveAsync(Guid, CancellationToken)`: return whether a memory was removed.
- `ClearAsync(CancellationToken)`: remove all memories; the store remains reusable.

Cancellation tokens are optional. Pre-canceled operations throw without changing
storage. Null memories and empty IDs are rejected explicitly. There is no update
operation in this milestone; adding or reading a memory does not rewrite its
timestamps.

[InMemoryMemoryStore](src/Snoopy.Voice.Console/Memories/InMemoryMemoryStore.cs)
uses an instance-owned dictionary guarded by a lock. Immutable records and
read-only snapshots prevent callers from mutating stored state outside that lock.
It remains available for unit tests with explicit DI injection, preserving the
original application-lifetime, read-only-snapshot and cancellation behavior.
It is not the real application's storage backend. There is no expiration or
automatic eviction in either store.

## Manual verification

### Persistence across restarts

1. Configure the dedicated database in local `appsettings.json` and run
   `--migrate-database` explicitly.
2. Run Snoopy normally. Say "Remember that I prefer .NET.", then "What do you
   remember?" Verify the saved content is confirmed and recalled.
3. Say "quit", start Snoopy again with the same configuration, then ask
   "What do you remember?" The saved memory must still be present.
4. Normal conversation history should start empty after restart. Conversation
   reset must not remove the persistent memory.

For intentional delete/clear testing, use a dedicated review database whose
contents you are prepared to change. These commands really delete saved memories;
restarting no longer empties the store.

### Natural-language memory across restarts

1. Enable `Snoopy:EnableMemoryContext` in local `appsettings.json`.
2. In Visual Studio, select the **Snoopy** profile, not the migration or speech-test
   profile, and start it with **Ctrl+F5**.
3. Say "Remember that my name is Akhil." and wait for the save confirmation.
4. Say "quit", restart Snoopy, then ask "Do you remember my name?"
5. Check for `[MEMORY] Using ... saved memories for this reply.` and a model answer
   using the saved name. The name must not depend on previous session history.
6. For a lookup-only cross-check, say "What do you remember about me?" This lists
   saved facts directly, regardless of whether memory context is enabled.

Offline regression tests verify the saved name reaches a fake model after creating
a fresh application/history, along with prompt limits, data escaping and spoken
outage warnings. Real model answers and audio playback still require this live check.

### Explicit memory workflow

With an initially empty review database, run these checks in order:

1. Say "Remember that I prefer .NET for backend development." Expect the saved
   content to be confirmed on screen and through TTS.
2. Say "What do you remember about me?" Expect that saved content, with no LLM call.
3. Say "Forget that I prefer .NET for backend development." Expect
   "Okay, I've forgotten that."
4. Say "What do you remember about me?" Expect
   "I don't have any saved memories yet."
5. Say "Remember that I prefer C#.", then "Clear my memories.", then
   "What do you remember?" Expect a clear confirmation followed by no memories.
   Repeat using "Forget everything you remember." as the clear command.
6. Say "I prefer .NET." without a command prefix. Expect a normal generated reply.
   Say "What do you remember?" and verify there are still no saved memories.
7. Save a memory, have a normal conversation, then say "reset conversation" or
   "new conversation". Verify explicit recall still returns the memory, but
   previous normal turns are no longer in model context. Conversely, start a
   multi-turn conversation, clear memories and verify the conversation continues
   with its previous turns.

The automated fake-service scenarios cover these workflows, but do not replace
live microphone, Azure connectivity and audible-playback checks.

### Existing voice behavior

1. Run `--speech-tests`. Test option **1** (several STT utterances; Enter stops),
   **2** (audible TTS), and **3** (recognize one sentence, stop microphone, echo).
   Option **4** exits. These paths retain the original implementation.
2. Run the normal conversation command. Say "Hello Snoopy", pause, and verify a
   generated response is both displayed and spoken before listening resumes.
3. Say "My name is Akhil", then "What is my name?" Verify the second response
   uses the context.
4. Say "reset conversation" and expect "Sure. I've started a new conversation."
   Ask "What is my name?" again. The model should no longer have the previous
   turns. Reset deliberately preserves the system prompt, including any facts
   you may have explicitly configured in it.
5. Tell Snoopy your name again, say "new conversation", and repeat the name
   question. Confirm that ordinary conversation and playback still work afterward.
6. Try silence or unclear speech: no empty LLM turn should be sent.
7. In separate runs, say "exit", "quit", "goodbye" and "stop"; verify a spoken
   farewell and clean exit for each. Also check an addressed form such as
   "Goodbye Snoopy".
8. In separate runs, press Ctrl+C during listening, an LLM request, and playback.
9. With deliberately invalid local LLM configuration, verify safe errors and
   continued listening. Restore valid settings afterward.
10. Check a TTS/device failure leaves the reply on-screen and permits another turn.

Unit tests use fake Speech/model services and a fake HTTP transport for the
**real official SDK**. They require no credentials, network, microphone or
speakers. They verify configuration, Azure request shape, error handling, bounded
history, non-mutating snapshots, serialized clearing, reset commands, sequential
multi-turn orchestration, exits, recovery and cancellation.
Memory tests cover CRUD operations, all categories, importance preservation, UTC
timestamps, validation, duplicate IDs, read-only snapshots, cancellation, concurrent
access, singleton lifetime and isolation from history. Model context remains
unchanged when the opt-in feature is disabled.
Explicit-command tests also cover all prefixes and aliases, case/whitespace,
incomplete input, category ambiguity, normalized full-text deletion, duplicate
content, not-found responses, recall ordering, independent resets, no automatic
extraction, model-call bypass, store failures and confirmation playback failures.
Memory-context tests also cover natural name recall after a fresh application,
keyword ranking, complete-fact and escaped-JSON budgets, duplicate selection,
untrusted-data boundaries, fresh reads after forget/clear, and spoken outage warnings.
They do **not** prove live Azure connectivity, microphone capture or audible playback.

### Optional PostgreSQL integration tests

Database integration tests are separated under
[Integration/](tests/Snoopy.Voice.Console.Tests/Integration) with the
`PostgreSQLIntegration` trait. They are **skipped by default** and never install,
create, migrate, clear during setup, or drop a database.

Opt-in requires an **already prepared, empty dedicated test database** on your
existing local PostgreSQL server, with the checked-in migration applied. Its name
must end in `_tests` (for example `snoopy_tests`); never point tests at `snoopy` or
another application's database. Generate the forward migration SQL with the
documented `migrations script` command, review it and apply it to the test
database in your usual local PostgreSQL administration tool.

In the same ignored `appsettings.json`, set `ConnectionStrings:SnoopyTestDatabase`
to that test database's connection string and set
`PostgreSQLIntegrationTests:Enabled` to `true`. These separate fields prevent
accidental use of the normal application database. Each test runs in a transaction
and rolls it back, including its test
inserts, deletes and clears. This verifies real PostgreSQL mapping, new-context
reads, duplicate rejection, deletion and clearing without committed test data.
Use the manual restart check above to verify committed persistence across processes.

```powershell
dotnet test .\tests\Snoopy.Voice.Console.Tests\Snoopy.Voice.Console.Tests.csproj `
    --no-build --filter "Category=PostgreSQLIntegration"
```

Return `PostgreSQLIntegrationTests:Enabled` to `false` afterward. No test connection
string or test-enabling environment variables are used.
No Azure credentials are needed. The default `dotnet test` run needs neither Azure
nor PostgreSQL; offline tests also verify EF model/migration shape, domain mapping,
single-file configuration, sanitized failures and voice-loop recovery.

## Architecture

```text
src/Snoopy.Voice.Console/
  Program.cs                       Startup, mode selection, Ctrl+C
  ApplicationServices.cs           Lightweight DI registrations (no host)
  ConsoleInput.cs                   Existing cancellable menu input
  VoiceConsoleApplication.cs        Original STT/TTS/echo test menu
  VoiceConversationApplication.cs   Sequential conversation/recovery loop
  Configuration/
    SpeechOptions.cs               Existing Speech configuration
    AzureOpenAIOptions.cs          Configurable Azure v1 deployment
    SnoopyOptions.cs               Prompt, history and opt-in memory-context bounds
    DatabaseOptions.cs             Validated, redacted local PostgreSQL settings
    ApplicationConfiguration.cs   Required local appsettings.json loading only
  Services/                       Existing Azure Speech STT/TTS and errors
  AI/
    ILanguageModelClient.cs        SDK-independent roles/messages and contract
    AzureOpenAILanguageModelClient.cs
    LanguageModelException.cs      Sanitized failure categories
    IConversationService.cs
    ConversationService.cs         Reply/clear orchestration and system prompt
    IConversationHistory.cs        History contract and complete-turn model
    InMemoryConversationHistory.cs Bounded, synchronized session storage
    IMemoryContextProvider.cs      Per-request saved-memory context contract
    MemoryContextProvider.cs       Bounded lexical selection and safe data formatting
    MemoryContext.cs               Transient instructions/data and outage warning
    ConversationCommands.cs        Reset commands and shared word normalization
    ExitCommands.cs
  Memories/
    Memory.cs                     Immutable identity, content and UTC metadata
    MemoryCategory.cs             Simple technology-independent categories
    IMemoryStore.cs                Async storage-only contract
    InMemoryMemoryStore.cs         Synchronized, application-lifetime memory
    IMemoryCommandParser.cs        Command detection contract, no storage
    MemoryCommandParser.cs         Deterministic prefixes and full command aliases
    MemoryCommandType.cs           None, Remember, Forget, Recall, Clear
    MemoryCommandResult.cs         Command type and extracted content
    MemoryText.cs                  Symbol-preserving comparison normalization
    IMemoryService.cs              Explicit memory business operations
    MemoryService.cs               Category rules and store coordination
    MemoryStorageException.cs      Sanitized persistence failure contract
  Persistence/
    MemoryEntity.cs                EF entity and domain-field mapping
    SnoopyDbContext.cs             Memories table mapping only
    PostgreSQLMemoryStore.cs       Per-operation contexts; IMemoryStore adapter
    MemoryDatabaseInitializer.cs   Explicit migration operation, never auto-started
    SnoopyDbContextFactory.cs      EF tooling without voice/Azure startup
    Migrations/                   InitialCreate and model snapshot
tests/Snoopy.Voice.Console.Tests/   Speech, conversation/SDK and offline memory tests
  Integration/                    Opt-in PostgreSQL tests, rollback-only
dotnet-tools.json                  Pinned local dotnet-ef tool
```

Packages:

- `Microsoft.CognitiveServices.Speech` **1.51.2**, unchanged.
- Official `OpenAI` **2.14.0**, using `OpenAI.Chat.ChatClient` and Azure's
  **v1 Chat Completions API**. Microsoft supports this SDK for the Azure v1
  endpoint; `Azure.AI.OpenAI` and dated API versions are not required.
- `Microsoft.Extensions.DependencyInjection` **10.0.12** and
  `Microsoft.Extensions.Configuration.Json` **10.0.12**.
- `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**, the PostgreSQL EF Core provider.
- `Microsoft.EntityFrameworkCore.Relational` **10.0.12**, explicitly aligning the
  runtime and transitive test dependencies with the design-time EF version.
- `Microsoft.EntityFrameworkCore.Design` **10.0.12**, a private tooling dependency;
  the local `dotnet-ef` tool is also pinned to **10.0.12**.
- Existing xUnit, .NET test SDK, Visual Studio runner and coverlet test packages,
  unchanged.

## Troubleshooting

| Symptom | Checks |
| --- | --- |
| Missing configuration | Fill the required fields in the ignored `appsettings.json` in the working directory. Run from the solution root. Use `--speech-tests` to test Speech alone. |
| Editing settings has no effect | Restart Snoopy and edit the file in the working directory, not a build-output copy. User Secrets and environment variables no longer override the file. |
| Missing/invalid PostgreSQL configuration | Set `ConnectionStrings:SnoopyDatabase` in local `appsettings.json` with Host, Database and Username. No fallback to in-memory storage occurs. |
| Memory storage is unavailable | Check that the existing local PostgreSQL service is running and the configured port, credentials, database and data permissions are correct. Normal model conversation can continue; failed memory operations are not reported as successful. |
| Migration fails | Check the dedicated database and role's create/schema permissions locally. Run `--migrate-database` explicitly after correcting configuration. Do not drop existing tables/databases to fix a mismatch. |
| Memories table missing | Run the checked-in migration against the same database used by normal startup. Voice startup does not apply schema changes. |
| Memories disappear after restart | Check that both runs use the same dedicated PostgreSQL database and the same local settings file. Conversation history is intentionally not persisted. |
| Saved name exists but the AI does not recall it | Enable `Snoopy:EnableMemoryContext`, restart, and use the normal Snoopy profile. Check the memory-context log and any outage warning. Use "What do you remember about me?" to cross-check the full store; bounded keyword selection may omit facts. |
| Integration tests skipped | Default behavior. Opt in only with a prepared, empty `*_tests` database and the documented test settings in local `appsettings.json`. |
| Headset connected but no speech | Select the **headset microphone** as Windows' default input; verify its level meter moves. Stop recognition and restart it after changing devices. |
| Microphone unavailable/no match | Enable Windows microphone access and desktop-app access. Check mute/input level and other apps using the device. Speak English and pause. |
| No audible playback | Check default output, volume/mute and headset/Bluetooth routing. Completion cannot prove you heard audio. |
| Speech authentication/connection error | Check ds-voice's key and `centralindia`; use a region identifier, not a URL. Check internet, DNS, TLS/WebSocket access on port 443 and firewall/proxy. |
| LLM authentication/access denied | Check the Azure OpenAI key belongs to the configured resource and resource/network access is permitted. |
| LLM deployment not found | Use the exact deployed name from Azure, not the model catalog name or model version date. |
| Rejected LLM request/context limit | Check Azure v1 Chat Completions compatibility, instruction role and model limits; reduce history/input budget as needed. |
| LLM response token limit | Increase `AzureOpenAI:MaxOutputTokens`, especially for reasoning models, or ask a smaller question. No truncated answer is added to history. |
| LLM timeout/rate limiting/server errors | Check connectivity, deployment quota and Azure health. Retry after waiting; adjust the request deadline if needed. |
| Older context is forgotten | Old conversation pairs are intentionally dropped after configured limits. Explicit PostgreSQL memories are separate and can be supplied through opt-in, bounded memory context. |
| SDK/native DLL load failure | Install the matching Visual C++ Redistributable, restore packages and rebuild using a supported Windows architecture. |
| Wrong SDK | Install .NET 10 and check `dotnet --version` from the solution root. |

References:
[Azure Speech platform requirements](https://learn.microsoft.com/azure/ai-services/speech-service/quickstarts/setup-platform?tabs=windows%2Cdotnet),
[Azure OpenAI v1 API](https://learn.microsoft.com/azure/ai-foundry/openai/api-version-lifecycle)
and [EF Core migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/).
