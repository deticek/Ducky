# Ducky

## Publishing

Ducky is published as self-contained packages, so users do not need to install
the .NET runtime. From Windows with the .NET 10 SDK installed, run:

```powershell
.\publish-all.ps1 -Version 1.0.0
```

Packages and SHA-256 checksums are written to `artifacts\release`. Supported
targets are Windows x64, ARM64, and x86; Linux x64 and ARM64 (glibc); and macOS
Intel and Apple Silicon. To build the Windows installer as well, install Inno
Setup 6 and run:

```powershell
.\publish-all.ps1 -Version 1.0.0 -CompileInstaller
```

`Ducky.iss` creates `Ducky-installer.exe`. The installer is per-user and
does not require administrator privileges. It registers `ducky` for Explorer,
adds “Open with Ducky” to directory and directory-background context menus,
and leaves user data in place when uninstalled.

To publish a GitHub release, create a release with a semantic version tag such
as `v1.0.0`. The release workflow uploads all platform ZIPs and checksums, the
Windows x64 Bootstrapper, and the Windows installer. Windows in-app updates
continue to use the `win-x64` package and its checksum. Linux and macOS users
download and replace their platform package manually; the Updates page states
this limitation.

## Platform notes

- Linux packages target glibc-based Linux x64 and ARM64 systems. They are not
  universal across every distribution or libc. Avalonia may require native
  desktop-system libraries from the distribution. Run the package with
  `sh ./Ducky.sh`; the launcher makes the bundled apphost executable.
- macOS packages target Intel and Apple Silicon. Run `sh ./Ducky.sh` after
  extracting the ZIP.
- On Windows, the installer registers the `ducky` command in Explorer's
  application lookup and adds context-menu actions for the current directory.
  The context menu passes the selected folder to Ducky as its initial
  temporary terminal directory.

User data is stored outside the install/package directory:

- Windows: `%LOCALAPPDATA%\Ducky`
- Linux: `$XDG_CONFIG_HOME/ducky` (default `~/.config/ducky`) and
  `$XDG_DATA_HOME/Ducky` (default `~/.local/share/Ducky`)
- macOS: `~/Library/Application Support/Ducky`

The config file is stored in each platform's configuration directory; themes,
workspace state, and logs use the data directory. Windows in-app updating is
provided by the self-contained Bootstrapper and does not require .NET to be
installed.
