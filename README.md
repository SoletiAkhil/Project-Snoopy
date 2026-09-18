# Snoopy Voice Assistant

A **.NET 10 C# console application** for sequential voice conversations:

```text
Default Windows microphone -> Azure Speech STT -> user's text
    -> Azure-hosted LLM + bounded conversation history
    -> assistant text -> Azure Speech TTS -> default Windows output
    -> listen for the next turn
```

Running without arguments starts a conversation. The original working STT,
TTS and echo services/menu are preserved: use **`--speech-tests`** to run them
independently, without Azure OpenAI configuration.

There is no MCP, tool calling, agent framework, persistent memory, database,
web API, device control or Raspberry Pi integration in this milestone.

## Prerequisites

- Windows and the **.NET 10 SDK**, not .NET 8 or 9. Check `dotnet --version`.
- Working default Windows microphone and audio output. When using a headset,
  select its microphone under Settings -> System -> Sound -> Input.
- Microsoft Visual C++ Redistributable for Visual Studio 2015-2022, matching
  the architecture of .NET, for the native Azure Speech SDK.
- Internet access to NuGet and your Azure services.
- Azure Speech resource **ds-voice**, region **Central India** (`centralindia`).
- An existing Azure OpenAI deployment supporting **v1 Chat Completions**.
  The initial target is **GPT-4.1 (2025-04-14)**. Its deployment name and model
  version are managed in Azure, not hardcoded in Snoopy.
- Local API keys for the respective resources. Usage may incur Azure charges.

Both projects target `net10.0`. `global.json` permits stable .NET 10 feature
bands, but not .NET 11.

## Configuration

### One secrets.json with .NET User Secrets (recommended for local development)

Snoopy uses Microsoft's **Secret Manager / User Secrets** configuration provider.
Keep your Speech key, model key, Azure endpoint, deployment, voice and optional
Snoopy settings together in one local file, **outside the project tree**:

```text
%APPDATA%\Microsoft\UserSecrets\93af160f-4e8e-4b4e-bd85-72408e39e0fc\secrets.json
```

A starter file has been created for this Windows user. Open it locally:

```powershell
notepad "$env:APPDATA\Microsoft\UserSecrets\93af160f-4e8e-4b4e-bd85-72408e39e0fc\secrets.json"
```

Fill in these four values and save:

```json
{
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

The actual starter uses **empty keys** so startup fails safely until you fill them.
It also includes the optional timeout, output-token, prompt and history settings.
Use the Azure **deployment name**, not the model-version date. Speech needs its
key and region; no Speech endpoint URL is required.

Then run Snoopy normally. **No application environment variables are required.**
Changes are read at startup, so restart Snoopy after editing the file. The same
store works from terminals and VS Code tasks running as your Windows user.
This local console app explicitly loads User Secrets in both Debug and Release;
it does not require `ASPNETCORE_ENVIRONMENT` or a web application host.

Configuration priority, highest first:

1. Existing named environment variables, if set.
2. .NET User Secrets.
3. Optional `appsettings.json` in the working directory.

If you previously set the six Azure environment variables, open a fresh terminal
or remove them from the terminal you will use so they do not override the file:

```powershell
Remove-Item Env:SNOOPY_SPEECH_KEY, Env:SNOOPY_SPEECH_REGION, Env:SNOOPY_SPEECH_VOICE,
    Env:AZURE_OPENAI_ENDPOINT, Env:AZURE_OPENAI_API_KEY, Env:AZURE_OPENAI_DEPLOYMENT `
    -ErrorAction SilentlyContinue
```

The `UserSecretsId` in the console project file is only an identifier, not a key,
and is safe to keep in source control. On a different computer/user account,
create the corresponding local store again; credentials do not travel with the
project. Nested JSON and Secret Manager's flattened keys (such as
`"AzureSpeech:ApiKey"`) are both supported.

**User Secrets are plaintext and intended for local development only.** They are
kept out of the repository and build output, but are not an encrypted vault.
Use a managed secret store/identity when deploying. Never share the local file
or the output of `dotnet user-secrets list`, which prints secret values.
The existing `.gitignore` also excludes any accidentally created project-local
`secrets.json`; a file in the project root is not the Secret Manager store.

### Environment variables (optional alternative)

Configure these in the **same PowerShell terminal** used to run Snoopy:

```powershell
Set-Location 'P:\Project snoopy'

$env:SNOOPY_SPEECH_REGION = "centralindia"
$env:SNOOPY_SPEECH_VOICE = "en-IN-NeerjaNeural"
$env:AZURE_OPENAI_ENDPOINT = "https://YOUR-RESOURCE.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT = "YOUR-DEPLOYMENT-NAME"

$speechKey = Read-Host 'Azure Speech key' -AsSecureString
$env:SNOOPY_SPEECH_KEY = [System.Net.NetworkCredential]::new('', $speechKey).Password
$speechKey.Dispose()
Remove-Variable speechKey

$modelKey = Read-Host 'Azure OpenAI key' -AsSecureString
$env:AZURE_OPENAI_API_KEY = [System.Net.NetworkCredential]::new('', $modelKey).Password
$modelKey.Dispose()
Remove-Variable modelKey
```

Paste keys only into the masked local prompts. Do not share them in chat or
put them in source code, task definitions, screenshots or issue reports.
Masked entry avoids putting a literal key in shell command history; the
resulting environment variable is still plaintext process configuration.
These settings apply **only to this PowerShell session and its child processes**.
They do not configure other terminals, VS Code tasks, or future sessions.

| Variable | Meaning when using environment configuration |
| --- | --- |
| `SNOOPY_SPEECH_KEY` | Required Speech resource key |
| `SNOOPY_SPEECH_REGION` | Required Speech region; `centralindia` for ds-voice |
| `SNOOPY_SPEECH_VOICE` | Optional; defaults to `en-IN-NeerjaNeural` |
| `AZURE_OPENAI_ENDPOINT` | Required in conversation mode; Azure resource root or its `/openai/v1/` URL |
| `AZURE_OPENAI_API_KEY` | Required in conversation mode; key for that Azure OpenAI resource |
| `AZURE_OPENAI_DEPLOYMENT` | Required in conversation mode; **deployment name**, which need not equal the model name |

Speech still uses a **key + region**, not an endpoint URL, and recognition is
**English (India), `en-IN`**. The voice setting changes TTS only. Programmatic
Speech options retain the `centralindia` default, but startup requires a region
provided explicitly via an environment variable or JSON setting.

For the LLM, both `https://RESOURCE.openai.azure.com/` and
`https://RESOURCE.services.ai.azure.com/` are supported. The adapter normalizes
either to `/openai/v1/`. Do not use a deployment-specific `/chat/completions`
request URL, query string, dated `api-version`, or a Foundry project URL.
HTTPS and the two Azure resource hostname suffixes are enforced to avoid
accidentally sending a key to a non-Azure host. Custom gateways and sovereign
cloud endpoints are not supported by this initial validator.

### Optional local JSON

If preferred, copy [appsettings.example.json](appsettings.example.json) to
`appsettings.json` in the **solution root** and edit your local copy:

```powershell
if (-not (Test-Path .\appsettings.json)) {
    Copy-Item .\appsettings.example.json .\appsettings.json
}
```

The example contains placeholders/empty keys only. Snoopy reads `appsettings.json`
from the **current working directory**. Environment variables override individual
JSON settings, and User Secrets override this file. An explicitly empty
environment variable does not fall back to a file secret. The local file is
ignored by Git, but it is not encrypted storage. Prefer User Secrets for keys
and use this file for non-secret settings. No `.env` file is loaded and secrets
are not copied into build output.

### Optional tuning and model changes

| Environment variable | JSON setting | Default |
| --- | --- | --- |
| `AZURE_OPENAI_TIMEOUT_SECONDS` | `AzureOpenAI:RequestTimeoutSeconds` | `45` |
| `AZURE_OPENAI_MAX_OUTPUT_TOKENS` | `AzureOpenAI:MaxOutputTokens` | `1024` |
| `AZURE_OPENAI_INSTRUCTION_ROLE` | `AzureOpenAI:InstructionRole` | `System` |
| `SNOOPY_SYSTEM_PROMPT` | `Snoopy:SystemPrompt` | Dedicated voice-friendly prompt |
| `SNOOPY_MAX_HISTORY_TURNS` | `Snoopy:MaxHistoryTurns` | `12` completed user/assistant pairs |
| `SNOOPY_MAX_HISTORY_CHARACTERS` | `Snoopy:MaxHistoryCharacters` | `24000` |
| `SNOOPY_MAX_INPUT_CHARACTERS` | `Snoopy:MaxInputCharacters` | `4000` |

To change models, update `AzureOpenAI:DeploymentName` in User Secrets (or
`AZURE_OPENAI_DEPLOYMENT` when using environment configuration) and restart.
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

# Original independent Speech test menu (no LLM configuration needed):
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --speech-tests

# Configuration/usage help without opening devices or contacting Azure:
dotnet run --project .\src\Snoopy.Voice.Console\Snoopy.Voice.Console.csproj --no-build -- --help
```

After editing code, rebuild or omit `--no-build` when running. VS Code includes
`build Snoopy`, `run Snoopy`, and `run Snoopy Speech tests` tasks; run tasks depend
on the build task. Tasks use the solution root as their working directory.
Variables set inside another terminal are not inherited by a new task terminal:
use User Secrets, the configured PowerShell terminal, or the ignored local JSON file.

Startup reports **configured**, not **connected**: validation and SDK construction
do not prove connectivity. Actual calls verify access when used. No real key,
raw HTTP response/header, SDK exception details or stack trace is printed.

## Conversation behavior

1. Snoopy listens for a final, non-empty STT result. Partial results and silence
   are never sent to the LLM. No-match results print a brief retry hint.
2. It stops/disposes the microphone iterator **before** LLM or TTS work.
3. The console shows `You: ...`. The conversation service sends the system
   prompt, retained history and current user message to Azure.
4. The console shows `Snoopy: ...` before playing the response.
5. Playback completes before the next microphone session begins. This prevents
   Snoopy from transcribing its own speaker output.

Say **exit**, **quit**, **goodbye** or **stop**, optionally addressed to Snoopy,
to hear "Goodbye! Talk to you later." and exit without an LLM call. Comparisons
ignore case and recognition punctuation. Ordinary sentences containing one of
these words do not exit. **Ctrl+C** cancels STT, an LLM call or TTS and shuts down.
There is no simultaneous listening/barge-in during LLM processing or playback.

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
and spoken error fallbacks are not stored. A displayed answer remains in history
even if its audio playback fails.

The oldest **whole pairs** are dropped to meet both the turn-count cap and the
character budget. The budget includes the system prompt and pending input for
each request; it also bounds stored history including the system prompt.
An individually oversized pair is dropped rather than retained as a half-turn.
This is a character-based bound, **not an exact tokenizer**. Choose limits that
fit the replacement model's context window, with room for output and protocol
overhead. Older facts can be forgotten after truncation. There is no summarizer
or persistent memory.

The app does not write conversations to disk and requests no stored chat
completion (`store: false`). Audio/text still go to the configured Azure services
for processing and are subject to those services' data-handling policies.

## Manual verification

1. Run `--speech-tests`. Test option **1** (several STT utterances; Enter stops),
   **2** (audible TTS), and **3** (recognize one sentence, stop microphone, echo).
   Option **4** exits. These paths retain the original implementation.
2. Run the normal conversation command. Say "Hello Snoopy", pause, and verify a
   generated response is both displayed and spoken before listening resumes.
3. Say "My name is Akhil", then "What is my name?" Verify the second response
   uses the context.
4. Try silence or unclear speech: no empty LLM turn should be sent.
5. Say "Goodbye Snoopy" and verify a spoken farewell and clean exit.
6. In separate runs, press Ctrl+C during listening, an LLM request, and playback.
7. With deliberately invalid local LLM configuration, verify safe errors and
   continued listening. Restore valid settings afterward.
8. Check a TTS/device failure leaves the reply on-screen and permits another turn.

Automated tests use fake Speech/model services and a fake HTTP transport for the
**real official SDK**. They require no credentials, network, microphone or
speakers. They verify configuration, Azure request shape, error handling, bounded
history, sequential multi-turn orchestration, exits, recovery and cancellation.
They do **not** prove live Azure connectivity, microphone capture or audible playback.

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
    SnoopyOptions.cs               Prompt and history bounds
    ApplicationConfiguration.cs   Environment/User Secrets/optional JSON loading
  Services/                       Existing Azure Speech STT/TTS and errors
  AI/
    ILanguageModelClient.cs        SDK-independent roles/messages and contract
    AzureOpenAILanguageModelClient.cs
    LanguageModelException.cs      Sanitized failure categories
    IConversationService.cs
    ConversationService.cs         Bounded in-memory history
    ExitCommands.cs
tests/Snoopy.Voice.Console.Tests/   Original Speech tests + conversation/SDK tests
```

Packages:

- `Microsoft.CognitiveServices.Speech` **1.51.2**, unchanged.
- Official `OpenAI` **2.14.0**, using `OpenAI.Chat.ChatClient` and Azure's
  **v1 Chat Completions API**. Microsoft supports this SDK for the Azure v1
  endpoint; `Azure.AI.OpenAI` and dated API versions are not required.
- `Microsoft.Extensions.DependencyInjection` **10.0.12** and
  `Microsoft.Extensions.Configuration.Json` **10.0.12**.
- `Microsoft.Extensions.Configuration.UserSecrets` **10.0.12** for Microsoft's
  local development secret store.
- Existing xUnit, .NET test SDK, Visual Studio runner and coverlet test packages,
  unchanged.

## Troubleshooting

| Symptom | Checks |
| --- | --- |
| Missing configuration | Fill the two keys, LLM endpoint/deployment and Speech region in User Secrets. Check that the current Windows user owns the configured store. Environment variables and local JSON remain alternatives. Use `--speech-tests` to test Speech alone. |
| Editing secrets has no effect | Restart Snoopy and remove stale environment overrides. Use the per-user store shown above, not a project-root file named secrets.json. |
| Headset connected but no speech | Select the **headset microphone** as Windows' default input; verify its level meter moves. Stop recognition and restart it after changing devices. |
| Microphone unavailable/no match | Enable Windows microphone access and desktop-app access. Check mute/input level and other apps using the device. Speak English and pause. |
| No audible playback | Check default output, volume/mute and headset/Bluetooth routing. Completion cannot prove you heard audio. |
| Speech authentication/connection error | Check ds-voice's key and `centralindia`; use a region identifier, not a URL. Check internet, DNS, TLS/WebSocket access on port 443 and firewall/proxy. |
| LLM authentication/access denied | Check the Azure OpenAI key belongs to the configured resource and resource/network access is permitted. |
| LLM deployment not found | Use the exact deployed name from Azure, not the model catalog name or model version date. |
| Rejected LLM request/context limit | Check Azure v1 Chat Completions compatibility, instruction role and model limits; reduce history/input budget as needed. |
| LLM response token limit | Increase `AZURE_OPENAI_MAX_OUTPUT_TOKENS`, especially for reasoning models, or ask a smaller question. No truncated answer is added to history. |
| LLM timeout/rate limiting/server errors | Check connectivity, deployment quota and Azure health. Retry after waiting; adjust the request deadline if needed. |
| Older context is forgotten | Old pairs are intentionally dropped after configured limits; this milestone has no persistent memory. |
| SDK/native DLL load failure | Install the matching Visual C++ Redistributable, restore packages and rebuild using a supported Windows architecture. |
| Wrong SDK | Install .NET 10 and check `dotnet --version` from the solution root. |

References:
[Azure Speech platform requirements](https://learn.microsoft.com/azure/ai-services/speech-service/quickstarts/setup-platform?tabs=windows%2Cdotnet),
[Azure OpenAI v1 API](https://learn.microsoft.com/azure/ai-foundry/openai/api-version-lifecycle)
and [Microsoft Secret Manager documentation](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

Remove keys from the current terminal after testing:

```powershell
Remove-Item Env:SNOOPY_SPEECH_KEY -ErrorAction SilentlyContinue
Remove-Item Env:AZURE_OPENAI_API_KEY -ErrorAction SilentlyContinue
```
