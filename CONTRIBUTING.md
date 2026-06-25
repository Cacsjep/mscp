# Contributing to MS Community Plugins

Thanks for your interest in contributing. This repository is a collection of
open-source plugins, Smart Client extensions, and device drivers for Milestone
XProtect, built and released as one solution with a unified installer.

This is an independent community project and is not affiliated with, endorsed by,
or supported by Milestone Systems.

## Ways to contribute

- **Report a bug:** open a [bug report](https://github.com/Cacsjep/mscp/issues/new?template=bug_report.md).
- **Request a feature:** open a [feature request](https://github.com/Cacsjep/mscp/issues/new?template=feature_request.md).
- **Ask or discuss:** use [GitHub Discussions](https://github.com/Cacsjep/mscp/discussions) or the [Discord](https://discord.gg/Geu5daeGPE).
- **Report a vulnerability:** do **not** open a public issue. Follow the
  [Security Policy](.github/SECURITY.md) (private reporting via the Security tab).

## Prerequisites

- Windows with **Visual Studio 2022** (or newer) and the **.NET Framework 4.7/4.8**
  developer pack.
- **.NET SDK** (for the standalone Auto Exporter components and the test projects).
- NuGet (bundled with Visual Studio) to restore packages.

The Milestone MIP SDK assemblies are pulled in via NuGet, so no separate SDK
install is required to build.

## Building

Open `MSCPlugins.sln` in Visual Studio, restore NuGet packages, and build, or
from a developer prompt:

```powershell
nuget restore MSCPlugins.sln
msbuild MSCPlugins.sln /p:Configuration=Release
```

A full local build (all plugins plus the MSI installer) is also driven by
`build.ps1`.

## Running the tests

The automated tests live under `Tests/` (`AutoExport.Tests`, `PKI.Vault.Tests`):

```powershell
dotnet test Tests/AutoExport.Tests/AutoExport.Tests.csproj
dotnet test Tests/PKI.Vault.Tests/PKI.Vault.Tests.csproj
```

Please add or update tests when you change behavior that is covered by a test
project.

## Pull request workflow

1. **Fork** the repository and create a feature branch off `main`
   (for example `fix/flexview-save`).
2. Make focused changes. Keep one logical change per pull request where possible.
3. Add a **changelog entry** at the top of `CHANGELOG.md` using the existing
   grammar so it renders correctly on the docs site:
   `Verb Component: description` (for example
   `Fix Flex View: Save no longer drops camera assignments`). Use
   `Add` / `Fix` / `Improve` / `Remove` / `Security` as the verb.
4. Open a pull request against `main`.
5. The **PR Build** check compiles every plugin and the installer. Your PR must be
   green (`PR Build / ci-success`) before it can be merged.

## Adding a new plugin or driver

New components are registered in `plugins.json`, which drives both the CI build
matrix and the installer generation. See
[Adding a New Plugin or Driver](README.md#adding-a-new-plugin-or-driver) in the
README for the step-by-step on wiring up a new plugin, driver, or Smart Client
extension.

## Coding style

Match the style of the surrounding code: existing naming, formatting, comment
density, and idioms. Keep changes minimal and avoid unrelated reformatting in the
same pull request.

## Documentation

User-facing docs live under `docs/` and are built with MkDocs Material. If your
change affects behavior, update the relevant page. You can preview locally with:

```powershell
pip install --require-hashes -r docs/requirements.txt
mkdocs serve
```

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
