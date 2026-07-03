param(
    [string]$DistDir = "dist-portable",
    [string[]]$Files = @(
        "PcMonitorOverlay.exe",
        "PcMonitorOverlayInstaller.exe",
        "PcMonitorOverlayUninstaller.exe"
    ),
    [string]$SignToolPath,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$FileDigest = "SHA256",
    [string]$TimestampDigest = "SHA256",
    [string]$PfxPath,
    [string]$PfxPassword = $env:CODESIGN_PFX_PASSWORD,
    [string]$CertThumbprint,
    [string]$CertSubject,
    [switch]$MachineStore,
    [switch]$AutoSelect,
    [switch]$SkipVerify,
    [switch]$ListCertificates,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Show-Help {
    Write-Host @"
Usage:
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-dist.ps1 [options]

Examples:
  # Use a PFX file. Prefer CODESIGN_PFX_PASSWORD over typing the password in history.
  `$env:CODESIGN_PFX_PASSWORD = "pfx-password"
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-dist.ps1 -PfxPath .\certs\codesign.pfx

  # Use a certificate from the CurrentUser certificate store by thumbprint.
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-dist.ps1 -CertThumbprint ABCD1234...

  # Use a certificate selected by subject name.
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-dist.ps1 -CertSubject "Your Publisher Name"

  # Let SignTool choose a suitable code signing certificate.
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-dist.ps1 -AutoSelect

Options:
  -DistDir             Distribution directory. Default: dist-portable
  -PfxPath             Path to a code signing .pfx certificate.
  -PfxPassword         PFX password. Defaults to CODESIGN_PFX_PASSWORD.
  -CertThumbprint      SHA-1 thumbprint of a certificate in the certificate store.
  -CertSubject         Subject name of a certificate in the certificate store.
  -MachineStore        Use LocalMachine certificate store instead of CurrentUser.
  -AutoSelect          Ask SignTool to select the best matching certificate.
  -TimestampUrl        RFC 3161 timestamp server URL. Default: http://timestamp.digicert.com
  -SkipVerify          Skip signature verification after signing.
  -ListCertificates    List local code signing certificates and exit.
"@
}

function Resolve-SignTool {
    if ($SignToolPath) {
        if (-not (Test-Path $SignToolPath)) {
            throw "SignTool was not found: $SignToolPath"
        }

        return [System.IO.Path]::GetFullPath($SignToolPath)
    }

    $pathCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($pathCommand) {
        return $pathCommand.Source
    }

    $candidateRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $candidateRoots) {
        $candidate = Get-ChildItem -Path $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "SignTool.exe was not found. Install Visual Studio or the Windows SDK, or pass -SignToolPath."
}

function Get-DistributionFiles {
    $distPath = Join-Path $repoRoot $DistDir
    if (-not (Test-Path $distPath -PathType Container)) {
        throw "Distribution directory was not found: $distPath"
    }

    $resolved = @()
    foreach ($file in $Files) {
        $path = Join-Path $distPath $file
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "Distribution file was not found: $path"
        }

        $resolved += [System.IO.Path]::GetFullPath($path)
    }

    return $resolved
}

function Get-SigningArguments {
    $arguments = @(
        "sign",
        "/fd", $FileDigest,
        "/tr", $TimestampUrl,
        "/td", $TimestampDigest
    )

    if ($MachineStore) {
        $arguments += "/sm"
    }

    if ($PfxPath) {
        if (-not (Test-Path $PfxPath -PathType Leaf)) {
            throw "PFX file was not found: $PfxPath"
        }

        $arguments += @("/f", [System.IO.Path]::GetFullPath($PfxPath))
        if ($PfxPassword) {
            $arguments += @("/p", $PfxPassword)
        }
    }
    elseif ($CertThumbprint) {
        $thumbprint = $CertThumbprint -replace "\s", ""
        $arguments += @("/sha1", $thumbprint)
    }
    elseif ($CertSubject) {
        $arguments += @("/n", $CertSubject)
    }
    else {
        $arguments += "/a"
    }

    return $arguments
}

function Show-CodeSigningCertificates {
    $stores = @("Cert:\CurrentUser\My")
    if ($MachineStore) {
        $stores = @("Cert:\LocalMachine\My")
    }

    foreach ($store in $stores) {
        if (-not (Test-Path $store)) {
            continue
        }

        Get-ChildItem $store |
            Where-Object {
                $_.HasPrivateKey -and
                $_.EnhancedKeyUsageList.FriendlyName -contains "Code Signing"
            } |
            Select-Object Subject, Thumbprint, NotAfter, Issuer |
            Format-Table -AutoSize
    }
}

if ($Help) {
    Show-Help
    exit 0
}

if ($ListCertificates) {
    Show-CodeSigningCertificates
    exit 0
}

$signTool = Resolve-SignTool
$targets = Get-DistributionFiles
$baseSignArguments = Get-SigningArguments

Write-Host "Using SignTool: $signTool" -ForegroundColor Cyan

foreach ($target in $targets) {
    Write-Host "Signing $target" -ForegroundColor Cyan
    & $signTool @baseSignArguments $target

    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed: $target"
    }
}

if (-not $SkipVerify) {
    foreach ($target in $targets) {
        Write-Host "Verifying $target" -ForegroundColor Cyan
        & $signTool verify /pa /v $target

        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed: $target"
        }
    }
}

Write-Host ""
Write-Host "Signing completed successfully." -ForegroundColor Green
