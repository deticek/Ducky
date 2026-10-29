
$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'test.csproj'
$releaseDirectory = Join-Path $PSScriptRoot 'release'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

# All supported release targets
$runtimes = @(
    @{ Id = 'win-x64';     OS = 'Windows'; Arch = 'x64' },
    @{ Id = 'win-arm64';   OS = 'Windows'; Arch = 'ARM64' },
    @{ Id = 'linux-x64';   OS = 'Linux';   Arch = 'x64' },
    @{ Id = 'linux-arm64'; OS = 'Linux';   Arch = 'ARM64' },
    @{ Id = 'osx-x64';     OS = 'macOS';   Arch = 'Intel' },
    @{ Id = 'osx-arm64';   OS = 'macOS';   Arch = 'AppleSilicon' }
)

# Create the release root
New-Item -ItemType Directory -Force -Path $releaseDirectory |
    Out-Null

foreach ($runtime in $runtimes) {
    $outputDirectory = Join-Path $releaseDirectory (
        Join-Path $runtime.OS $runtime.Arch
    )

    Write-Host ''
    Write-Host "========================================"
    Write-Host "Publishing Ducky for $($runtime.OS) ($($runtime.Id))"
    Write-Host "Output: $outputDirectory"
    Write-Host "========================================"

    if (Test-Path -LiteralPath $outputDirectory) {
        Remove-Item -LiteralPath $outputDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $outputDirectory |
        Out-Null

    $publishArguments = @(
        'publish',
        $projectFile,
        '--configuration', 'Release',
        '--runtime', $runtime.Id,
        '--self-contained', 'true',
        '--output', $outputDirectory,
        '-p:AssemblyName=Ducky'
    )

    & dotnet @publishArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Ducky publish failed for runtime: $($runtime.Id)"
    }

    # Add a launcher for Linux and macOS
    if ($runtime.OS -in @('Linux', 'macOS')) {
        $launcherPath = Join-Path $outputDirectory 'Ducky.sh'

        $launcherLines = @(
            '#!/usr/bin/env sh',
            'set -eu',
            'APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"',
            'cd "$APP_DIR"',
            'exec "$APP_DIR/Ducky" "$@"'
        )

        Set-Content `
            -LiteralPath $launcherPath `
            -Value ($launcherLines -join "`n") `
            -NoNewline `
            -Encoding ascii
    }

    Write-Host "Successfully published $($runtime.Id)"
}

Write-Host ''
Write-Host "All Ducky releases are available in:"
Write-Host $releaseDirectory
