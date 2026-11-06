param([string]$Version = '0.4.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号格式应为 0.1.0' }
$releaseName = "ICT盲点表工具-v$Version-win-x64"
$releaseRoot = Join-Path $PSScriptRoot 'releases'
$publishDir = Join-Path $releaseRoot $releaseName
$archive = Join-Path $releaseRoot "$releaseName.zip"
if ((Test-Path -LiteralPath $publishDir) -or (Test-Path -LiteralPath $archive)) { throw '该版本已存在，请使用新版本号。' }
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetExe = if ($dotnetCommand) { $dotnetCommand.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }
& $dotnetExe publish (Join-Path $PSScriptRoot 'src\ICTCoverage.WinForms\ICTCoverage.WinForms.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$Version" -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'publish 失败' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $publishDir 'docs') -Recurse
$validationDir = Join-Path $publishDir 'validation'
New-Item -ItemType Directory -Force -Path $validationDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'outputs\test-results\regression.trx') -Destination $validationDir
$sourceHashes = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'config') -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash
$metadata = [ordered]@{ version=$Version; releaseDate='2026-11-06'; target='win-x64'; selfContained=$true; ruleStatus='2.2.1; category grouping; reason catalog CRUD and history; unknown causes allowed; single workbook export'; sourceHashes=$sourceHashes }
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publishDir 'release-info.json') -Encoding UTF8
$manifest = Get-ChildItem -LiteralPath $publishDir -Recurse -File | Sort-Object FullName | ForEach-Object { $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; '{0}  {1}' -f $hash.Hash,$_.FullName.Substring($publishDir.Length+1) }
$manifest | Set-Content -LiteralPath (Join-Path $publishDir 'SHA256SUMS.txt') -Encoding UTF8
Compress-Archive -LiteralPath $publishDir -DestinationPath $archive -CompressionLevel Optimal
Write-Host "发布目录：$publishDir"
Write-Host "压缩包：$archive"
