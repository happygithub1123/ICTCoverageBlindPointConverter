param([string]$FolderName = 'ICT盲点表工具-登录优化本地试用-20261109')
$ErrorActionPreference = 'Stop'
if ($FolderName -match '[\\/]' -or $FolderName -in '.', '..') { throw '请指定单个目录名。' }
$destination = Join-Path (Join-Path $PSScriptRoot 'releases') $FolderName
if (Test-Path -LiteralPath $destination) { throw '目录已存在，请使用其他名称以保留旧版本。' }
$dotnetExe = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnetExe publish (Join-Path $PSScriptRoot 'src\ICTCoverage.WinForms\ICTCoverage.WinForms.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -o $destination
if ($LASTEXITCODE -ne 0) { throw '本地构建失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $destination 'docs') -Recurse
$validation = Join-Path $destination 'validation'
New-Item -ItemType Directory -Path $validation | Out-Null
foreach ($name in @('login-profile-baseline-controlled.json','login-paint-baseline-controlled.json','login-resources-baseline-controlled.json','login-profile-local-fix.json','login-paint-local-fix.json','login-resources-local-fix.json','scroll-latest.json','performance-latest.json','v055-login-1200.png')) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'outputs') $name) -Destination $validation
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'outputs\test-results\regression-local-login.trx') -Destination $validation
$sourceHashes = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'config') -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash
[ordered]@{ kind='local-preview'; date='2026-11-09'; baseVersion='0.5.5'; selfContained=$true; target='win-x64'; githubUpload=$false; sourceHashes=$sourceHashes } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'local-build-info.json') -Encoding UTF8
$manifest = Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object { $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; '{0}  {1}' -f $hash.Hash,$_.FullName.Substring($destination.Length+1) }
$manifest | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding UTF8
Write-Output $destination
# Deliberately no GitHub operations or changes to existing release directories.
