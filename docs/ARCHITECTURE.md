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

## Dependency Direction
- `Presentation` -> `Application`, `Domain`, `Infrastructure`
- `Application` -> `Domain`, `Infrastructure`
- `Domain` -> none
- `Infrastructure` -> framework/external SDKs, and `Application` abstractions when needed

## Current Mapping
- `Presentation/Windows`
Window XAML and UI event handlers.
- `Application/Ai`
AI analysis coordinator and provider abstractions.
- `Application/PostAnalysis`
Post-processing service interfaces and implementation.
- `Application/Validation`
Runtime validation for recognizer configuration.
- `Domain/Models`
Recognized text item model.
- `Infrastructure/Audio`
Recorder and device wrappers.
- `Infrastructure/Recognizers`
Vosk, Google STT, Web Speech adapters and recognizer interfaces.
- `Infrastructure/Ai`
OpenAI provider implementation.
- `Infrastructure/Server`
Local HTTP server for Web Speech bridge.

## Why This Structure
- Separates UI concerns from integration code.
- Makes provider replacement and testing boundaries explicit.
- Reduces cognitive load by grouping code by responsibility.
