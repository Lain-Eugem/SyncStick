param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\SyncStick.App\SyncStick.App.csproj'
$dist = Join-Path $root 'dist\SyncStick'
$zip = Join-Path $root 'dist\SyncStick.zip'

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'

Push-Location $root
try {
    # A stale MSBuild node can make nested restores fail with no useful error.
    dotnet build-server shutdown | Out-Null
    Start-Sleep -Milliseconds 500

    dotnet restore $appProject --nologo -m:1
    if ($LASTEXITCODE -ne 0) {
        dotnet build-server shutdown | Out-Null
        Start-Sleep -Milliseconds 800
        dotnet restore $appProject --nologo -m:1
    }

    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

    dotnet build $appProject -c Release --no-restore --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

    # The workspace path contains an apostrophe, which trips MSB3094 when
    # publish writes directly into it. Publish to a neutral temp path first.
    $staging = Join-Path $env:TEMP ("syncstick-publish-" + [guid]::NewGuid().ToString('N'))
    try {
        dotnet publish $appProject -c Release -o $staging --no-restore --nologo -m:1
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

        if (Test-Path $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $dist | Out-Null
        Copy-Item -Path (Join-Path $staging '*') -Destination $dist -Recurse -Force
        Get-ChildItem -Path $dist -Filter '*.pdb' | Remove-Item -Force
        Copy-Item -Path (Join-Path $root 'docs\运行说明.txt') -Destination $dist -Force
        Copy-Item -Path (Join-Path $root 'docs\运行说明-EN.txt') -Destination $dist -Force
    }
    finally {
        if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
    }

    if (-not $NoZip) {
        if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
        Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force
    }

    Write-Host ''
    Write-Host 'Release ready:' -ForegroundColor Green
    Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
    if (-not $NoZip) { Write-Host "Zip: $zip" }
}
finally {
    Pop-Location
}
