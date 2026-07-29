[CmdletBinding()]
param(
    [ValidateSet('Validate', 'BuildRelease', 'PackageRelease', 'Clean')]
    [string]$Action = 'Validate',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.2'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$releaseRoot = Join-Path $workspaceRoot 'release\ScreenshotCat_Windows\release'
$publishRoot = Join-Path $releaseRoot "App-v$Version"
$project = Join-Path $projectRoot 'ScreenshotCat\ScreenshotCat.csproj'
$verification = Join-Path $projectRoot 'ScreenshotCat.Verification\ScreenshotCat.Verification.csproj'
$installerScript = Join-Path $projectRoot 'installer\ScreenshotCat.iss'

$sdkRoot = Join-Path $workspaceRoot 'sdk'
$env:DOTNET_CLI_HOME = Join-Path $sdkRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $sdkRoot 'nuget-packages'
$env:TEMP = Join-Path $workspaceRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP | Out-Null

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: dotnet $($Arguments -join ' ')"
    }
}
function Assert-ReleasePath {
    param([Parameter(Mandatory)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $expected = [System.IO.Path]::GetFullPath($releaseRoot)
    if (-not $fullPath.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside release root: $fullPath"
    }
}

function Get-SizeOptimizedPublishProperties {
    @(
        "-p:Version=$Version",
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=true',
        '-p:TrimMode=partial',
        '-p:PublishReadyToRun=false',
        '-p:WindowsAppSDKSelfContained=false',
        '-p:WindowsPackageType=None',
        '-p:SatelliteResourceLanguages=en',
        '-p:DebugType=none',
        '-p:DebugSymbols=false',
        '-p:DebuggerSupport=false',
        '-p:EnableCompressionInSingleFile=false'
    )
}

function Remove-PublishBloat {
    param([Parameter(Mandatory)][string]$Root)
    $patterns = @(
        'onnxruntime*.dll',
        'DirectML*.dll',
        'Microsoft.ML.OnnxRuntime.dll',
        'Microsoft.Windows.AI*.dll',
        'Microsoft.Windows.Widgets*.dll',
        'System.Numerics.Tensors.dll',
        '*.pdb',
        'mscordaccore*.dll',
        'mscordbi.dll',
        'createdump.exe',
        'dbgshim.dll',
        'Microsoft.DiaSymReader*.dll',
        'clretwrc.dll'
    )
    foreach ($pattern in $patterns) {
        Get-ChildItem -LiteralPath $Root -Filter $pattern -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
}

function Invoke-Validation {
    Invoke-Dotnet @(
        'build', $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        "-p:Version=$Version",
        '-p:WindowsAppSDKSelfContained=false',
        '-p:WindowsPackageType=None'
    )
    Invoke-Dotnet @(
        'run', '--project', $verification,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        "-p:Version=$Version"
    )
}

function Invoke-ReleaseBuild {
    Assert-ReleasePath $publishRoot
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
    Invoke-Dotnet (@(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishRoot
    ) + (Get-SizeOptimizedPublishProperties))

    # WinUI's unpackaged publish target can omit compiled XAML/PRI resources.
    # Copy the exact resources produced by the matching Release build so the
    # published executable can resolve App.xaml and every window at runtime.
    $pri = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'ScreenshotCat\bin\Release') `
        -Filter 'ScreenshotCat.pri' -Recurse -File |
        Where-Object { $_.DirectoryName.EndsWith('\win-x64', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $pri) {
        throw 'Release build did not produce ScreenshotCat.pri.'
    }
    Copy-Item -LiteralPath $pri.FullName -Destination $publishRoot
    Get-ChildItem -LiteralPath $pri.DirectoryName -Filter '*.xbf' -File |
        Copy-Item -Destination $publishRoot

    Remove-PublishBloat -Root $publishRoot

    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $publishRoot
    $docsAssets = Join-Path $publishRoot 'assets'
    New-Item -ItemType Directory -Force -Path $docsAssets | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\sponsor.jpg') -Destination $docsAssets
}

function New-ReleaseArchive {
    $archive = Join-Path $releaseRoot "ScreenshotCat-v$Version-win-x64.zip"
    $checksum = "$archive.sha256"
    Assert-ReleasePath $archive
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath $checksum -Encoding ascii
    $appBytes = (Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Measure-Object Length -Sum).Sum
    [pscustomobject]@{
        Path = $archive
        Bytes = (Get-Item -LiteralPath $archive).Length
        SHA256 = $hash
        AppBytes = $appBytes
    }
}

function Get-InnoSetupCompiler {
    $candidates = @(
        $env:INNO_SETUP_COMPILER,
        (Join-Path $sdkRoot 'Inno Setup 7\ISCC.exe'),
        (Join-Path $sdkRoot 'Inno\ISCC.exe'),
        (Join-Path $sdkRoot 'InnoSetup7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $compiler = $candidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $compiler) {
        throw 'Inno Setup compiler not found. Install it under D:\vibecoding\sdk\Inno Setup 7 or set INNO_SETUP_COMPILER.'
    }
    return [IO.Path]::GetFullPath($compiler)
}

function New-SetupPackage {
    if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
        throw "Installer script not found: $installerScript"
    }

    $compiler = Get-InnoSetupCompiler
    $outputBaseName = "ScreenshotCat-v$Version-win-x64-setup"
    $setup = Join-Path $releaseRoot "$outputBaseName.exe"
    $checksum = "$setup.sha256"
    Assert-ReleasePath $setup
    Remove-Item -LiteralPath $setup, $checksum -Force -ErrorAction SilentlyContinue

    & $compiler `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishRoot" `
        "/DOutputDir=$releaseRoot" `
        "/DOutputBaseFilename=$outputBaseName" `
        $installerScript | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
        throw "Inno Setup did not create the expected package: $setup"
    }

    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $setup)" | Set-Content -LiteralPath $checksum -Encoding ascii
    [pscustomobject]@{
        Path = $setup
        Bytes = (Get-Item -LiteralPath $setup).Length
        SHA256 = $hash
    }
}

function Clear-BuildCache {
    $targets = @(
        (Join-Path $projectRoot 'ScreenshotCat\bin'),
        (Join-Path $projectRoot 'ScreenshotCat\obj'),
        (Join-Path $projectRoot 'ScreenshotCat.Verification\bin'),
        (Join-Path $projectRoot 'ScreenshotCat.Verification\obj')
    )
    foreach ($target in $targets) {
        $fullPath = [System.IO.Path]::GetFullPath($target)
        if (-not $fullPath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean path outside project root: $fullPath"
        }
        if (Test-Path -LiteralPath $fullPath) {
            Remove-Item -LiteralPath $fullPath -Recurse -Force
        }
    }
}

switch ($Action) {
    'Validate' {
        Invoke-Validation
    }
    'BuildRelease' {
        Invoke-ReleaseBuild
    }
    'PackageRelease' {
        Invoke-Validation
        Invoke-ReleaseBuild
        $archive = New-ReleaseArchive
        $setup = New-SetupPackage
        [pscustomobject]@{
            Version = $Version
            Configuration = 'Release'
            AppBytes = $archive.AppBytes
            Archive = $archive
            Setup = $setup
        } | ConvertTo-Json -Depth 4
    }
    'Clean' {
        Clear-BuildCache
    }
}
