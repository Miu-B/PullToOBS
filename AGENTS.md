# Agent Guidelines

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
- Use interfaces (IOBSController, IIINACTClient) for testability
- Save config via injected save delegate, not static PluginInterface access
