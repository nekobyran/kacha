[CmdletBinding()]
param(
    [ValidateSet('Stage', 'Validate', 'Deploy', 'Status')]
    [string]$Action = 'Validate'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$websiteRoot = Join-Path $projectRoot 'website'
$stageRoot = Join-Path $websiteRoot '.dist'
$configPath = Join-Path $projectRoot 'wrangler.jsonc'
$workerPath = Join-Path $projectRoot 'worker.js'
$expectedSponsorHash = '1E23933B0C5DA7169FFBBC64EF58B324867ADA4EA38CF1F772F2CF13BA5C300A'
$repository = 'nekobyran/kacha'
$repositoryUrl = "https://github.com/$repository"
$releaseApiUrl = 'https://kacha.nkbr.cc/api/release'
$siteUrl = 'https://kacha.nkbr.cc/'
$publicFiles = @('index.html', '404.html', 'styles.css', 'script.js', 'robots.txt', '_headers', 'assets\app-icon.webp', 'assets\sponsor.jpg')

function Assert-StagePath {
    $full = [IO.Path]::GetFullPath($stageRoot)
    $expected = [IO.Path]::GetFullPath((Join-Path $websiteRoot '.dist'))
    if (-not [string]::Equals($full, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected stage path: $full"
    }
}

function Invoke-Stage {
    Assert-StagePath
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
    foreach ($relative in $publicFiles) {
        $source = Join-Path $websiteRoot $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Missing public file: $relative"
        }
        $target = Join-Path $stageRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    "stage=pass;files=$($publicFiles.Count);path=$stageRoot"
}

function Assert-SiteSource {
    $index = Get-Content -LiteralPath (Join-Path $websiteRoot 'index.html') -Raw -Encoding utf8
    $script = Get-Content -LiteralPath (Join-Path $websiteRoot 'script.js') -Raw -Encoding utf8
    $styles = Get-Content -LiteralPath (Join-Path $websiteRoot 'styles.css') -Raw -Encoding utf8
    $headers = Get-Content -LiteralPath (Join-Path $websiteRoot '_headers') -Raw -Encoding utf8
    $worker = Get-Content -LiteralPath $workerPath -Raw -Encoding utf8
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding utf8 | ConvertFrom-Json

    foreach ($required in @(
        'Kacha — Windows 截图与批注工具',
        'data-release-version="latest"',
        $repositoryUrl,
        "$repositoryUrl/blob/main/LICENSE",
        'MIT License',
        'data-release-title',
        'data-release-file',
        'data-release-size',
        'data-release-link',
        'data-checksum-command',
        'Windows 10 1809+',
        '完全本地处理',
        'id="workflow"',
        'id="faq"',
        'assets/sponsor.jpg'
    )) {
        if (-not $index.Contains($required, [StringComparison]::Ordinal)) {
            throw "Release page missing required content: $required"
        }
    }
    if (([regex]::Matches($index, 'data-download')).Count -ne 2) {
        throw 'Release page must expose exactly two dynamic download entries.'
    }
    if ($index.Contains('/releases/download/', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release page must not hard-code a release asset URL.'
    }
    if ($index.Contains('github.com/nekobyran/ScreenshotCat', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release page still references the renamed ScreenshotCat repository.'
    }
    if ($index -match '<img\b[^>]*\bsrc\s*=\s*["'']\s*["'']') {
        throw 'Release page contains an empty image source.'
    }
    if ($index -match '(?i)private preview|test build|verification status|smoke|测试版|测试状态|验证状态') {
        throw 'Release page contains internal testing or preview wording.'
    }
    if ($index -match '(?i)(api[_-]?key|access[_-]?token|client[_-]?secret|password)\s*[:=]') {
        throw 'Release page contains a possible secret field.'
    }
    foreach ($required in @('/api/release', 'RELEASES_FALLBACK', 'document.querySelectorAll(''img'')', 'visibilitychange', 'revealAll', 'observer.disconnect')) {
        if (-not $script.Contains($required, [StringComparison]::Ordinal)) {
            throw "Release script missing required behavior: $required"
        }
    }
    if ($script -match '(?i)(eval\s*\(|innerHTML\s*=|document\.write\s*\()') {
        throw 'Release page script uses a forbidden dynamic HTML primitive.'
    }
    if (-not $styles.Contains('prefers-reduced-motion', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release styles missing reduced-motion behavior.'
    }
    foreach ($required in @('ambient-orbits', '@keyframes ambient-orbit')) {
        if (-not $styles.Contains($required, [StringComparison]::Ordinal)) {
            throw "Release styles missing unified organic motion marker: $required"
        }
    }
    if ($styles -notmatch '(?i)overflow-x\s*:\s*hidden') {
        throw 'Release styles missing horizontal-overflow protection.'
    }
    foreach ($required in @(
        "const REPOSITORY = 'nekobyran/kacha'",
        '/releases/latest',
        'GITHUB_TIMEOUT_MS = 8_000',
        'AbortController',
        'caches.default',
        'cache.put',
        'ScreenshotCat-.*-win-x64-setup',
        'Content-Security-Policy',
        'no-transform'
    )) {
        if (-not $worker.Contains($required, [StringComparison]::Ordinal)) {
            throw "Worker missing required release/security marker: $required"
        }
    }
    if ($headers -notmatch 'Content-Security-Policy:' -or $headers -notmatch 'Strict-Transport-Security:') {
        throw 'Static headers must include CSP and HSTS.'
    }
    if ($config.name -ne 'kacha-release' -or $config.main -ne './worker.js' -or $config.assets.directory -ne './website/.dist') {
        throw 'Wrangler Worker/static asset configuration is incorrect.'
    }
    if ($config.routes.Count -ne 1 -or $config.routes[0].pattern -ne 'kacha.nkbr.cc' -or -not $config.routes[0].custom_domain) {
        throw 'Wrangler custom domain configuration is incorrect.'
    }
    $sponsor = Join-Path $websiteRoot 'assets\sponsor.jpg'
    if ((Get-FileHash -LiteralPath $sponsor -Algorithm SHA256).Hash -cne $expectedSponsorHash) {
        throw 'Sponsor image hash does not match the verified source.'
    }
    foreach ($relative in $publicFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $websiteRoot $relative) -PathType Leaf)) {
            throw "Missing public file: $relative"
        }
    }
    'validation=pass;domain=kacha.nkbr.cc;release=dynamic;repository=nekobyran/kacha;license=MIT;downloads=2;webview=progressive;security=pass'
}

function Invoke-Validate {
    Assert-SiteSource
}

function Invoke-Wrangler {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $npmCache = Join-Path $workspaceRoot 'cache\npm'
    New-Item -ItemType Directory -Force -Path $npmCache | Out-Null
    $env:npm_config_cache = $npmCache
    $npx = (Get-Command 'npx.cmd' -ErrorAction Stop).Source
    $global:LASTEXITCODE = 0
    & $npx --yes 'wrangler@4.112.0' @Arguments
    if ($global:LASTEXITCODE -ne 0) {
        throw "Wrangler failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Invoke-Status {
    $response = Invoke-WebRequest -Uri $siteUrl -TimeoutSec 30
    if ($response.StatusCode -ne 200 -or -not $response.Content.Contains('data-release-version="latest"')) {
        throw 'Production site did not return the dynamic release page.'
    }
    if ($response.Content.Contains('github.com/nekobyran/ScreenshotCat', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Production site still references the renamed repository.'
    }
    foreach ($header in @('Content-Security-Policy', 'Strict-Transport-Security', 'X-Content-Type-Options')) {
        if (-not $response.Headers.ContainsKey($header)) {
            throw "Production response is missing security header: $header"
        }
    }
    $cacheControl = [string]$response.Headers['Cache-Control']
    if (-not $cacheControl.Contains('no-transform', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Production HTML response is missing no-transform.'
    }
    $releaseResponse = Invoke-WebRequest -Uri $releaseApiUrl -TimeoutSec 30
    if ($releaseResponse.StatusCode -ne 200) { throw 'Production release API did not return HTTP 200.' }
    $release = $releaseResponse.Content | ConvertFrom-Json
    if (-not $release.ok -or $release.repository -ne $repository -or $release.license.spdx -ne 'MIT') {
        throw 'Production release API returned incorrect repository or license metadata.'
    }
    if (-not $release.asset -or $release.asset.name -notmatch '^ScreenshotCat-.*-win-x64-setup\.exe$') {
        throw 'Production release API did not resolve the Windows x64 Setup asset.'
    }
    if (-not ([string]$release.asset.downloadUrl).StartsWith("https://github.com/$repository/releases/download/", [StringComparison]::Ordinal)) {
        throw 'Production release API returned an unexpected download origin.'
    }
    $asset = Invoke-WebRequest -Uri ([string]$release.asset.downloadUrl) -Method Head -MaximumRedirection 8 -TimeoutSec 60
    if ($asset.StatusCode -ne 200) { throw 'Resolved GitHub release asset is unavailable.' }
    "status=pass;url=$siteUrl;http=200;release=$($release.tag);asset=$($release.asset.name);repository=$repository;license=MIT;security=pass"
}

switch ($Action) {
    'Stage' { Invoke-Stage }
    'Validate' { Invoke-Validate }
    'Deploy' {
        Invoke-Stage
        Invoke-Validate
        Invoke-Wrangler -Arguments @('whoami')
        Invoke-Wrangler -Arguments @('deploy', '--config', $configPath)
    }
    'Status' { Invoke-Status }
}
