param(
    [Parameter(Mandatory=$true)] [string]$Version,
    [string]$PreviousNupkgPath   # optional: path to last release's -full.nupkg for delta generation
)

$ErrorActionPreference = "Stop"
$SourceDir   = $PSScriptRoot
$PublishDir  = "$SourceDir\publish_temp"
$ReleasesDir = "$SourceDir\Releases"
$AppId       = "InfinLimit"
$ExeName     = "InfinLimit.exe"

Write-Host "=== InfinLimit v$Version ===" -ForegroundColor Cyan

# 1. Publish single-file self-contained exe
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish "$SourceDir\InfinLimit.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $PublishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Write-Host "Published." -ForegroundColor Green

# 2. vpk pack (with optional delta generation from previous nupkg)
if (Test-Path $ReleasesDir) { Remove-Item $ReleasesDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null

if ($PreviousNupkgPath -and (Test-Path $PreviousNupkgPath)) {
    Copy-Item $PreviousNupkgPath $ReleasesDir -Force
    Write-Host "Seeded previous package for delta: $(Split-Path $PreviousNupkgPath -Leaf)" -ForegroundColor DarkCyan
}

vpk pack -u $AppId -v $Version -p $PublishDir -e $ExeName `
    --packTitle "InfinLimit" --noPortable -o $ReleasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Remove-Item $PublishDir -Recurse -Force
Write-Host "Done: $ReleasesDir\${AppId}-win-Setup.exe" -ForegroundColor Green
