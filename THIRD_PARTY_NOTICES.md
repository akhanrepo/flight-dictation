# Third-party notices

Flight's own code is MIT-licensed (see [LICENSE](LICENSE)). It builds on the projects below, each under its own license. Check the linked license for the authoritative terms; this list is a convenience.

## Libraries (NuGet)

| Package | Used for | License |
|---|---|---|
| [Whisper.net](https://github.com/sandrohanea/whisper.net) (+ `Whisper.net.Runtime`, bundling [whisper.cpp](https://github.com/ggerganov/whisper.cpp)) | Speech-to-text | MIT |
| [LLamaSharp](https://github.com/SciSharp/LLamaSharp) (+ `LLamaSharp.Backend.Cpu`, bundling [llama.cpp](https://github.com/ggerganov/llama.cpp)) | Local LLM inference | MIT |
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | WinUI 3 shell | MIT |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | Windows SDK build tooling | Microsoft Software License |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) | DI, configuration, logging | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) (bundling [SQLite](https://sqlite.org)) | Local database | MIT (SQLite: public domain) |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | DPAPI encryption of the API key | MIT |
| [NAudio.WinMM](https://github.com/naudio/NAudio) | Microphone capture | MIT |
| [Velopack](https://github.com/velopack/velopack) | Installer and auto-update | MIT |

Test-only: xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Microsoft.Extensions.TimeProvider.Testing (MIT).

## Models (downloaded on first run, not redistributed in this repository)

| Model | Source | License |
|---|---|---|
| Whisper base, multilingual (`ggml-base-q5_1.bin`) | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp), converted from [OpenAI Whisper](https://github.com/openai/whisper) | MIT |
| Phi-4-mini-instruct, Q4_K_M GGUF | [bartowski/microsoft_Phi-4-mini-instruct-GGUF](https://huggingface.co/bartowski/microsoft_Phi-4-mini-instruct-GGUF), quantized from [microsoft/Phi-4-mini-instruct](https://huggingface.co/microsoft/Phi-4-mini-instruct) | MIT |

## Fonts and icons

The UI uses the Windows system font (Segoe UI Variable) and Segoe Fluent Icons, both supplied by Windows and not redistributed. The Flight logo and icons in `assets/brand/` are original and covered by this repository's MIT license.
