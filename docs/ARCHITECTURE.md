# NextVoiceSync Architecture

## Layers
- `Presentation`
UI and code-behind (`MainWindow`, `PromptEditDialog`) only.
- `Application`
Use-case oriented logic such as analysis orchestration, post-analysis workflow, and validation.
- `Domain`
Pure models and business-friendly data structures.
- `Infrastructure`
External integrations and adapters (audio capture, recognizers, AI provider, HTTP server).

## Project Split
- `NextVoiceSync` (`net8.0-windows`)
Windows GUI application (WPF).
- `NextVoiceSync.Core` (`net8.0`)
Cross-platform reusable domain/application/infrastructure (except Windows-only adapters).
- `NextVoiceSync.Cli` (`net8.0`)
Cross-platform CLI entrypoint that consumes `NextVoiceSync.Core`.

## Dependency Direction
- `Presentation` -> `Application`, `Domain`, `Infrastructure`
- `Application` -> `Domain`, `Infrastructure`
- `Domain` -> none
- `Infrastructure` -> framework/external SDKs, and `Application` abstractions when needed

## Current Mapping
- `Presentation/Windows`
Window XAML and UI event handlers.
- `NextVoiceSync.Core/Application/Ai`
AI analysis coordinator and provider abstractions.
- `NextVoiceSync.Core/Application/PostAnalysis`
Post-processing service interfaces and implementation.
- `NextVoiceSync.Core/Application/Validation`
Runtime validation for recognizer configuration.
- `NextVoiceSync.Core/Domain/Models`
Recognized text item model.
- `Infrastructure/Audio`
Recorder and device wrappers.
- `Infrastructure/Recognizers`
Vosk, Google STT, Web Speech adapters and recognizer interfaces.
- `NextVoiceSync.Core/Infrastructure/Ai`
OpenAI provider implementation.
- `Infrastructure/Server`
Local HTTP server for Web Speech bridge.

## Why This Structure
- Separates UI concerns from integration code.
- Makes provider replacement and testing boundaries explicit.
- Reduces cognitive load by grouping code by responsibility.
