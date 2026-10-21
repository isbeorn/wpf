# Windows host safeguards, 21 October 2026

The user restored Windows after Explorer, Start and Task Manager failed with
`0xc0000017`. The incident audit identified repeated .NET SDK first-run additions
to persistent user PATH as the supported cause. The approved cleanup removed 218
WPF tool-path entries. See
`Documentation/portability/windows-host-incident-2026-10-21.md`.

- The user explicitly authorized resuming NINA implementation and Windows-side
  testing after the cleanup. This is an exception to the earlier Ubuntu-only
  restriction. Keep artifacts and investigation reports inside the workspace.
- Before every .NET SDK invocation, set `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false`
  and `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` in the invocation's process
  environment, inherited by all children. Do not persist these variables.
- Capture raw user and machine PATH before and after Windows toolchain batches.
  Stop further execution and report the exact difference if either changes.
  Use the guarded portability entry points. Apply the same
  safeguards to ad hoc commands and generated helper scripts.
- `DOTNET_CLI_HOME`, a private desktop and temporary directories are not host
  isolation boundaries. Do not rely on `DOTNET_SKIP_FIRST_TIME_EXPERIENCE` to
  prevent modern SDK first-run side effects. Never reproduce the oversized-PATH
  failure on this host.
- Do not install, update, replace, remove or repair Windows system, shared or
  user-level .NET runtimes or SDKs. Do not change host registry settings,
  persistent environment variables, services, drivers, boot settings or ACLs.
- Any further host repair or persistent configuration change requires a new
  explicit user instruction for the concrete action. Testing authorization does
  not grant it.
- Apply these restrictions to every delegated agent and every execution route.
- GitHub writes require explicit human confirmation of the concrete target and
  changes. Keep this work local.
