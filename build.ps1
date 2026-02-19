param(
    [Alias("a")]
    [string]$Arch = "x86-64",
    [Alias("v")]
    [string]$Version = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Show-Usage {
    Write-Host "Usage: .\\build.ps1 [-a|--arch <x86-64|x64|amd64|arm64|aarch64>] [-v|--version <major.minor.patch[.build]>]" -ForegroundColor Yellow
}

if ($Help) {
    Show-Usage
    exit 0
}

if ($args.Count -gt 0) {
    Write-Host "Unknown argument: $($args[0])" -ForegroundColor Red
    Show-Usage
    exit 1
}

$archNormalized = $Arch.ToLowerInvariant()
$singBoxVersion = "1.12.17"
$extendedVersion = "1.5.3"
$singBoxRelease = "$singBoxVersion-extended-$extendedVersion"
$singBoxBaseUrl = "https://github.com/shtorm-7/sing-box-extended/releases/download/v$singBoxRelease"

if ($Version -and $Version -notmatch "^\d+\.\d+\.\d+(\.\d+)?$") {
    Write-Host "Unsupported version format: $Version. Use major.minor.patch[.build]" -ForegroundColor Red
    exit 1
}

switch -Regex ($archNormalized) {
    "^(x86-64|x64|amd64)$" {
        $rid = "win-x64"
        $platform = "x64"
        $singBoxArch = "amd64"
        break
    }
    "^(arm64|aarch64)$" {
        $rid = "win-arm64"
        $platform = "arm64"
        $singBoxArch = "arm64"
        break
    }
    default {
        Write-Host "Unsupported arch: $Arch" -ForegroundColor Red
        exit 1
    }
}

$singBoxUrl = "$singBoxBaseUrl/sing-box-$singBoxRelease-windows-$singBoxArch.zip"

$config = "Release"
$tfm = "net10.0-windows10.0.19041.0"

function Update-SingBoxBinary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SingBoxUrl
    )

    $scriptRoot = Split-Path -Parent $PSCommandPath
    $resourcesDir = Join-Path $scriptRoot "Onibox/Resources"
    $targetPath = Join-Path $resourcesDir "sing-box.exe"
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("onibox-sing-box-" + [System.Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $tempRoot "sing-box.zip"
    $extractDir = Join-Path $tempRoot "extract"

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        Write-Host "Downloading sing-box release..."
        Invoke-WebRequest -Uri $SingBoxUrl -OutFile $zipPath

        Write-Host "Extracting sing-box.exe..."
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

        $singBoxExe = Get-ChildItem -Path $extractDir -Recurse -File | Where-Object { $_.Name -ieq "sing-box.exe" } | Select-Object -First 1
        if (-not $singBoxExe) {
            throw "sing-box.exe not found in downloaded archive: $SingBoxUrl"
        }

        New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
        Copy-Item -Path $singBoxExe.FullName -Destination $targetPath -Force
        Write-Host "Updated: Onibox/Resources/sing-box.exe"
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -Path $tempRoot -Recurse -Force
        }
    }
}

Update-SingBoxBinary -SingBoxUrl $singBoxUrl

$publishArgs = @(
    "publish"
    "Onibox/Onibox.csproj"
    "-c"
    $config
    "-r"
    $rid
    "--self-contained"
    "true"
    "/p:RuntimeIdentifiers=$rid"
    "/p:Platform=$platform"
    "/p:PlatformTarget=$platform"
)

if ($Version) {
    $fileVersion = $Version
    if ($Version -match "^\d+\.\d+\.\d+$") {
        $fileVersion = "$Version.0"
    }

    $publishArgs += "/p:Version=$Version"
    $publishArgs += "/p:AssemblyVersion=$fileVersion"
    $publishArgs += "/p:FileVersion=$fileVersion"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Publish ready: Onibox/bin/$config/$tfm/$rid/publish/"
