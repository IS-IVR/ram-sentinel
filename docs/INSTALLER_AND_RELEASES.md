# Installer and release distribution

## What the release contains

A versioned Windows x64 release contains three assets:

- **RAM-Sentinel-Setup-<version>-x64.exe**: Inno Setup installer.
- **RAM-Sentinel-Portable-<version>-x64.zip**: the same self-contained x64 publish output, without an installer.
- **SHA256SUMS.txt**: SHA-256 hashes for the installer and portable ZIP.

The installer is per-user. Its default destination is **%LOCALAPPDATA%\Programs\RAM Sentinel**; it does not require administrator elevation. It creates a Start Menu shortcut and offers an optional desktop shortcut. It does not create a service, scheduled task, automatic Windows-start entry, application-specific registry setting, or system-wide installation. The normal Windows uninstall record and uninstaller are provided by Inno Setup.

The installer offers an optional launch checkbox after setup. It does not silently launch the application.

## Release gate

The release workflow is triggered only by a pushed version tag matching **vMAJOR.MINOR.PATCH**, for example **v1.2.3**. It then:

1. Confirms that the tagged commit is reachable from **main**. A tag on an unmerged feature branch is rejected.
2. Restores and builds the full solution on Windows.
3. Runs Core checks, Windows process-inventory integration checks, and the inventory soak smoke check.
4. Publishes the self-contained **win-x64** application.
5. Compiles the Inno Setup installer and creates the portable ZIP.
6. Generates SHA-256 checksums.
7. Publishes a GitHub Release with generated release notes.

Ordinary pushes and pull requests do not publish release assets. This implementation does not create a version tag or publish a release. A maintainer must create a version tag manually, and only after the relevant pull requests are merged and the manual UI and 30-minute low-resource Windows soak checks are accepted.

## Installing and verifying

For the installer, run the downloaded setup EXE as the current Windows user. No administrator approval should be needed. To use the portable version, extract the ZIP to a normal writable folder and run **RamSentinel.exe**.

To verify the downloaded files in PowerShell, compare the output of:

~~~powershell
Get-FileHash .\RAM-Sentinel-Setup-1.2.3-x64.exe -Algorithm SHA256
Get-FileHash .\RAM-Sentinel-Portable-1.2.3-x64.zip -Algorithm SHA256
~~~

with the matching lines in **SHA256SUMS.txt**. Replace **1.2.3** with the released version.

A checksum detects accidental corruption or an unexpected file change relative to the published checksum. It is not a digital signature and does not prove publisher identity.

## Signing status

The installer and application are currently unsigned. No code-signing certificate or protected signing secret is configured by this workflow. Windows SmartScreen may therefore show an unknown-publisher or reputation warning. Do not describe the release as signed or SmartScreen-reputable unless signing is added and verified separately.

## Release download counts

The repository is private. Release assets are available only to people with repository access, and download counts are not a public badge or public analytics endpoint. GitHub exposes a **download_count** for each release asset to authorized maintainers through the Releases API.

With GitHub CLI authenticated to an account that can read this repository, run:

~~~powershell
gh api repos/IS-IVR/ram-sentinel/releases/tags/v1.2.3 --jq '.assets[] | "\(.name)\t\(.download_count)"'
~~~

The result lists the individual asset name and its accumulated download count. Replace the tag with the release you want to inspect. Counts are per asset, not a unique-person count; repeated downloads can increase them.

## Safety and scope

The installer only copies the published application files and creates shortcuts plus the standard uninstaller entry. It does not change Windows services, security settings, paging configuration, startup entries, or user files. It does not enable lifecycle recommendations or automated process actions.
