---
name: upgrade-nuget-packages
description: Upgrade all NuGet dependencies to their latest stable versions, validate the full solution, and open a pull request.
---

# Upgrade NuGet packages

Use this skill when asked to upgrade the repository's NuGet dependencies. Do not treat this file as an instruction to perform an upgrade merely because it exists.

## Prepare

1. Inspect the worktree and current branch. If there are uncommitted changes, stop and ask the user how to proceed; do not include or discard their work.
2. Identify the intended PR base. Use the user-specified base if given; otherwise use the repository's default branch. Fetch it and create a uniquely named `chore/upgrade-nuget-packages` branch from its current remote tip before editing. Do not overwrite an existing branch.
3. Inspect the current solution, project files, `Directory.Packages.props`, NuGet configuration, CI, and any PR template. Read the actual target frameworks and package declarations rather than relying on potentially outdated documentation. Currently, both projects target .NET 10, central package management is enabled, and CI installs .NET 9; adjust the CI SDK if necessary to build the actual targets.

## Upgrade

1. Inventory all explicitly declared NuGet package versions, including every `PackageVersion` in `Directory.Packages.props`, even if not currently referenced, and any versioned project references or overrides. Preserve central package management.
2. Check configured NuGet sources for the latest stable version of every package, including major releases. For example, use `dotnet package list --project ColumnEncryptor.sln --outdated` on a suitable SDK for referenced packages, and check the sources separately for any central entries it does not report. Do not assume a framework-filtered outdated report proves a package is at its latest published version. Honor configured sources and source mapping; never expose credentials.
3. Update all declarations to their latest stable versions without downgrading. Keep related packages compatible and make only the code, test, configuration, or CI changes required by the upgrades. Preserve encryption behavior and compatibility with existing encrypted data. Do not silently settle for an older version or omit an incompatible package. If a latest version requires a framework upgrade or another material scope change, ask the user before making it; if it cannot be resolved, stop and report the blocker instead of opening a partial-upgrade PR.
4. Recheck every declaration against the sources after editing. If no updates are available, report that and stop without creating an empty commit or PR.

## Validate and publish

1. Use an SDK supporting the actual target frameworks. The full tests use Testcontainers with HashiCorp Vault and SQL Server, so ensure Docker is available and responsive, with permission to pull and run the required images. Do not substitute filtered or mocked tests for the full suite.
2. Run the full solution restore and Release build, then the full test suite, for example:

   ```bash
   dotnet restore ColumnEncryptor.sln
   dotnet build ColumnEncryptor.sln --configuration Release --no-restore
   dotnet test ColumnEncryptor.sln --configuration Release --no-build --verbosity normal
   ```

   Verify tests were actually discovered and executed, and inspect failures and skips. Fix upgrade-related failures and rerun the affected checks and full suite. If prerequisites are unavailable, tests fail, or skipped tests prevent establishing that the full suite passed, report the limitation and do not open a PR.
3. Review the diff for unrelated changes, verify that every declared package is at the latest stable version, and check that CI can build and run the tests using the target SDK. Do not include unrelated work or secrets.
4. Only after all checks pass, commit the changes, push the new branch, and create a PR targeting the chosen base using the available GitHub tooling and any repository PR template. Follow applicable commit and PR instructions. Summarize the package upgrades, required compatibility changes, and validation results in the PR. If pushing or PR creation fails, report the failure without claiming a PR was created.
