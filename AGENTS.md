# Windows host restriction, 21 October 2026

The user restored Windows after Explorer, Start and Task Manager failed with
`0xc0000017`. The cause has not been established. NINA implementation and
qualification remain stopped until the recorded command history has been audited
and the user authorizes continuation.

- Use this Windows host only for source editing, Git operations and read-only
  investigation. Keep investigation reports inside the workspace.
- Run all builds, tests, application probes, reference runs, package-consumer
  checks and profiling in the Ubuntu environment. Do not run them on this host.
- Do not install, update, replace, remove or repair Windows system, shared or
  user-level .NET runtimes or SDKs. Do not change host registry settings,
  persistent environment variables, services, drivers, boot settings or ACLs.
- A host repair or an exception requires a new explicit user instruction for
  the concrete action. General authorization to continue NINA does not grant it.
- Apply these restrictions to every delegated agent and every execution route.
- GitHub writes require explicit human confirmation of the concrete target and
  changes. Keep this work local.
