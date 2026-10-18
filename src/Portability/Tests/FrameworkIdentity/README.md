# Framework identity and kernel behavior

This console suite references the source-built framework through a separate
companion assembly. It uses ordinary references and the Default load context,
without a custom loader or an assembly alias.

The observable checks cover app-local source identity, dependency property
default/set/clear/callback behavior, validation and coercion, Freezable cloning,
freezing and write rejection, thread affinity and frozen cross-thread sharing,
real resource error text, XAML construction across the companion boundary and
the absence of a WindowsDesktop runtime requirement.

Run from `src/Portability`:

```sh
dotnet run --project Tests/FrameworkIdentity/Runner/Runner.csproj
```

The runner emits JSON with one result per check and returns 1 if any fail.
Set NUGET_PACKAGES and DOTNET_CLI_HOME to workspace artifact directories before
building. Results prove the tested kernel behavior only; they do not establish
that native WindowsBase features or the WPF visual stack are portable.
