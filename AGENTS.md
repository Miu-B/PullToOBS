# Agent Guidelines

## Git Policy
- **NEVER commit without explicit user approval.** Always present the staged changes and proposed commit message, then wait for the user to confirm before running `git commit`.
- **NEVER push to the remote repository without explicit user approval.** After committing, ask the user before running `git push`. This includes tags.
- **NEVER create or push tags without explicit user approval.**
- These rules apply even when the user says "continue" or "proceed" — committing and pushing require separate, explicit approval each time.

## Build Commands
- Build: `dotnet build --configuration Release` (or Debug)
- Test: `dotnet test`

## Code Style
- Indent: 4 spaces, LF, final newline required
- Braces: New line before open brace
- Modifiers: public, private, protected, internal, new, abstract, virtual, sealed, override, static, readonly, extern, unsafe, async
- New line before catch/else/finally blocks

## Naming Conventions
- Types/methods/properties: PascalCase
- Private instance fields: camelCase with `_` prefix
- Events: PascalCase with `On` prefix
- Private static fields/constants: PascalCase
- Prefer `var` for built-in types and when type is apparent

## Patterns
- Use `[PluginService]` for Dalamud service injection
- Implement `IDalamudPlugin` for main plugin class
- Mark config classes with `[Serializable]`
- All windows and services must implement `IDisposable`
- Use constructor injection for dependencies (IPluginLog, interfaces)
- Use interfaces (IOBSController) for testability
- Save config via injected save delegate, not static PluginInterface access
