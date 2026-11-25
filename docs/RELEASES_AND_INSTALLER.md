# Installer and GitHub Releases

## Distribution model

RAM Sentinel is distributed as Windows x64 in two forms:

1. **Setup installer** — a per-user installation under `%LOCALAPPDATA%\Programs\RAM Sentinel`. It creates a Start Menu shortcut, offers an optional Desktop shortcut, and registers a normal uninstaller for the current user.
2. **Portable ZIP** — contains the self-contained `RamSentinel.exe`; extract it and run it directly.

The installer does not request administrator privileges. It does not install a Windows service, create a scheduled task, add an automatic startup entry, change system-wide registry settings, disable security features, or modify Windows optimization settings.

## Build locally

Requirements: Windows x64, .NET 10 SDK, and Inno Setup 6.

From the repository root:

```powershell
dotnet restore .\ram-sentinel.sln
dotnet build .\ram-sentinel.sln --configuration Release --no-restore
dotnet publish .\src\RamSentinel.App\RamSentinel.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output .\artifacts\win-x64
.\scripts\Build-Release.ps1 -AppVersion 0.1.0
```

The script creates:

- `artifacts/release/RAM-Sentinel-Setup-0.1.0-win-x64.exe`
- `artifacts/release/RAM-Sentinel-Portable-0.1.0-win-x64.zip`
- `artifacts/release/SHA256SUMS.txt`

The packaging script checks that the published executable and Inno Setup compiler exist, checks the compiler exit code, verifies the installer output, and calculates SHA-256 hashes for both distributable files.

## Automated CI and publishing

The Build and Test workflow builds and tests the application on pushes and pull requests. Installer and Portable ZIP packaging steps are skipped on ordinary branch and pull-request CI runs; distributable packages are created only for a pushed version tag matching `v*`, after the Windows build and test steps pass.

For a version tag, the Windows job compiles the installer, creates the Portable ZIP, generates SHA-256 checksums, then performs a silent per-user install/uninstall smoke test. The smoke test verifies that the installed executable is byte-for-byte identical to the published executable and that the per-user uninstall registration is created and removed. The release job checks out the tagged commit and verifies that it is reachable from `main` before uploading the three assets with generated release notes. A tag that points only to an unmerged feature branch is rejected. The workflow does not create tags.

Do not create a public version tag until the project’s manual Windows validation and low-resource soak requirements are satisfied. In particular, PR #8 remains dependent on the Windows UI check and 30-minute soak documented in Issue #9. The step-by-step checklist is maintained in [docs/WINDOWS_MANUAL_VALIDATION.md](WINDOWS_MANUAL_VALIDATION.md).

## Download counts

The repository is currently private, so this project intentionally does not display a public third-party download badge. GitHub exposes a `download_count` field for each Release asset. Authorized maintainers can inspect these per-asset counts through the GitHub Releases UI or the authenticated GitHub REST API. The count represents asset downloads, not unique people, active installations, repository clones, or downloads of temporary Actions artifacts.

The official GitHub REST response includes the `download_count` field: https://docs.github.com/en/rest/releases/assets.

## Signing status

The installer is currently unsigned. This work does not add Authenticode signing, a signing certificate, or a SmartScreen reputation claim. A Windows warning may appear for an unsigned or newly distributed file. Code signing must be implemented as a separate release-security task using a protected certificate and GitHub Actions secrets; never commit a certificate or private key to the repository.
