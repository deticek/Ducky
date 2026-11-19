param(
    [string]$Version,
    [switch]$CompileInstaller
)

$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'Ducky.csproj'
$bootstrapperProject = Join-Path $PSScriptRoot 'Ducky.Bootstrapper\Ducky.Bootstrapper.csproj'
$releaseDirectory = Join-Path $PSScriptRoot 'artifacts\release'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectFile
    $Version = [string]($project.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    )
}

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$') {
    throw "Version must be semantic, for example 1.2.3 or 1.2.3-beta.1."
}

$runtimes = @(
    @{ Id = 'win-x64';     Name = 'Windows x64' },
    @{ Id = 'win-arm64';   Name = 'Windows ARM64' },
    @{ Id = 'win-x86';     Name = 'Windows x86' },
    @{ Id = 'linux-x64';   Name = 'Linux x64 (glibc)' },
    @{ Id = 'linux-arm64'; Name = 'Linux ARM64 (glibc)' },
    @{ Id = 'osx-x64';     Name = 'macOS Intel' },
    @{ Id = 'osx-arm64';   Name = 'macOS Apple Silicon' }
)

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null

foreach ($runtime in $runtimes) {
    $runtimeDirectory = Join-Path $releaseDirectory $runtime.Id
    $appDirectory = Join-Path $runtimeDirectory 'App'
    $packagePath = Join-Path $releaseDirectory (
        "Ducky-$Version-$($runtime.Id).zip"
    )

    Write-Host ''
    Write-Host "Publishing $($runtime.Name) ($($runtime.Id))"

    if (Test-Path -LiteralPath $appDirectory) {
        Remove-Item -LiteralPath $appDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $appDirectory | Out-Null
    $publishArguments = @(
        'publish',
        $projectFile,
        '--configuration', 'Release',
        '--runtime', $runtime.Id,
        '--self-contained', 'true',
        '--output', $appDirectory,
        "-p:Version=$Version",
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Ducky publish failed for runtime: $($runtime.Id)"
    }

    $manifest = @{
        version = $Version
        runtime = $runtime.Id
        entryPoint = if ($runtime.Id.StartsWith('win-')) {
            'Ducky.exe'
        } else {
            'Ducky'
        }
    } | ConvertTo-Json -Compress
    Set-Content `
        -LiteralPath (Join-Path $appDirectory 'release-manifest.json') `
        -Value $manifest `
        -Encoding utf8

    if (-not $runtime.Id.StartsWith('win-')) {
        $launcher = @(
            '#!/usr/bin/env sh',
            'set -eu',
            'APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"',
            'chmod +x "$APP_DIR/Ducky"',
            'cd "$APP_DIR"',
            'exec "$APP_DIR/Ducky" "$@"'
        ) -join "`n"
        Set-Content `
            -LiteralPath (Join-Path $appDirectory 'Ducky.sh') `
            -Value $launcher `
            -NoNewline `
            -Encoding ascii
    }

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    Compress-Archive `
        -Path (Join-Path $appDirectory '*') `
        -DestinationPath $packagePath `
        -CompressionLevel Optimal

    $checksumResult = Get-FileHash `
        -LiteralPath $packagePath `
        -Algorithm SHA256
    $checksum = $checksumResult.Hash.ToLowerInvariant()
    Set-Content `
        -LiteralPath ($packagePath + '.sha256') `
        -Value $checksum `
        -NoNewline `
        -Encoding ascii

    Write-Host "Created $packagePath"
}

$bootstrapperDirectory = Join-Path $releaseDirectory 'win-x64\Bootstrapper'
if (Test-Path -LiteralPath $bootstrapperDirectory) {
    Remove-Item -LiteralPath $bootstrapperDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $bootstrapperDirectory | Out-Null

$bootstrapperArguments = @(
    'publish',
    $bootstrapperProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $bootstrapperDirectory,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:Version=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
& dotnet @bootstrapperArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Ducky Bootstrapper publish failed for win-x64.'
}

$bootstrapperPackage = Join-Path `
    $releaseDirectory `
    'Ducky.Bootstrapper-win-x64.exe'
Copy-Item `
    -LiteralPath (Join-Path $bootstrapperDirectory 'Ducky.Bootstrapper.exe') `
    -Destination $bootstrapperPackage `
    -Force
$bootstrapperHashResult = Get-FileHash `
    -LiteralPath $bootstrapperPackage `
    -Algorithm SHA256
$bootstrapperChecksum = $bootstrapperHashResult.Hash.ToLowerInvariant()
Set-Content `
    -LiteralPath ($bootstrapperPackage + '.sha256') `
    -Value $bootstrapperChecksum `
    -NoNewline `
    -Encoding ascii

if ($CompileInstaller) {
    $compiler = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -eq $compiler) {
        $knownCompilerPath = Join-Path `
            ${env:ProgramFiles(x86)} `
            'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $knownCompilerPath) {
            $compilerPath = $knownCompilerPath
        } else {
            throw 'Inno Setup 6 (ISCC.exe) is required to compile Ducky.iss.'
        }
    } else {
        $compilerPath = $compiler.Source
    }

    Push-Location $PSScriptRoot
    try {
        & $compilerPath "/DAppVersion=$Version" '.\Ducky.iss'
        if ($LASTEXITCODE -ne 0) {
            throw 'Ducky installer compilation failed.'
        }
    } finally {
        Pop-Location
    }
}

Write-Host ''
Write-Host "Release packages are available in: $releaseDirectory"
