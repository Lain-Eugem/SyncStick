param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
$mingw = 'C:\mingw64\bin'
$env:PATH = "$mingw;$env:PATH"

function Invoke-Step {
    param([string]$Name, [string]$Exe, [string[]]$Arguments)
    Write-Host "==> $Name"
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $build | Out-Null
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Invoke-Step 'compile core' 'g++' @(
    '-std=c++20', '-municode', '-Wall', '-Wextra', '-O2', '-Isrc',
    '-c', 'src/core.cpp', '-o', 'build/core.o'
)
Invoke-Step 'compile ui' 'g++' @(
    '-std=c++20', '-municode', '-Wall', '-Wextra', '-O2', '-Isrc',
    '-c', 'src/main.cpp', '-o', 'build/main.o'
)
Invoke-Step 'compile resources' 'windres' @(
    'src/resources.rc', '-O', 'coff', '-o', 'build/resources.o'
)
Invoke-Step 'link' 'g++' @(
    '-std=c++20', '-municode', 'build/main.o', 'build/core.o', 'build/resources.o',
    '-o', 'build/SyncStickPortable.exe', '-mwindows', '-static',
    '-lcomctl32', '-lole32', '-luuid', '-lshell32', '-lshlwapi',
    '-lgdi32', '-luser32', '-ladvapi32'
)

if (-not $SkipTests) {
    Invoke-Step 'compile tests' 'g++' @(
        '-std=c++20', '-municode', '-Wall', '-Wextra', '-Isrc',
        'tests/core_test.cpp', 'build/core.o', '-o', 'build/core_test.exe'
    )
    Invoke-Step 'run tests' (Join-Path $build 'core_test.exe') @()
}

$package = Join-Path $dist 'SyncStickPortable'
if (Test-Path $package) {
    Remove-Item -Recurse -Force $package
}
New-Item -ItemType Directory -Force -Path $package | Out-Null
Copy-Item (Join-Path $build 'SyncStickPortable.exe') $package
Copy-Item (Join-Path $root 'packaging\autorun.inf') $package
Copy-Item (Join-Path $root 'packaging\说明.txt') $package
Copy-Item (Join-Path $root 'README.md') $package
Copy-Item -Recurse (Join-Path $root 'docs') $package

$zip = Join-Path $dist 'SyncStickPortable.zip'
if (Test-Path $zip) {
    Remove-Item -Force $zip
}
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal

$exe = Get-Item (Join-Path $package 'SyncStickPortable.exe')
Write-Host ''
Write-Host "package : $package"
Write-Host "zip     : $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
Write-Host "exe     : $($exe.Length) bytes, version $($exe.VersionInfo.FileVersion)"
