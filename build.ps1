param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetExe = if ($dotnetCommand) { $dotnetCommand.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetExe)) { throw '请安装 .NET 8 SDK。' }
Push-Location $PSScriptRoot
try {
    & $dotnetExe restore ICTCoverageBlindPointConverter.sln
    if ($LASTEXITCODE -ne 0) { throw 'restore 失败' }
    & $dotnetExe build ICTCoverageBlindPointConverter.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'build 失败' }
    & $dotnetExe test ICTCoverageBlindPointConverter.sln -c $Configuration --no-build --logger 'trx;LogFileName=regression.trx' --results-directory outputs/test-results
    if ($LASTEXITCODE -ne 0) { throw 'test 失败' }
} finally { Pop-Location }
