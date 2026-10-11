param([string]$WorkDirectory, [string]$PreviewPath)
$ErrorActionPreference = 'Stop'
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $env:TEMP ('ClickDeck-tests-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$source = Split-Path -Parent $PSScriptRoot
$product = Split-Path -Parent $source
& (Join-Path $source 'build.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$target = Join-Path $WorkDirectory 'TestTarget.exe'
$runner = Join-Path $WorkDirectory 'TestRunner.exe'
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll')
& $compiler /nologo /target:winexe /codepage:65001 @references ('/out:' + $target) (Join-Path $PSScriptRoot 'TestTarget.cs')
if ($LASTEXITCODE -ne 0) { throw '测试窗口编译失败。' }
& $compiler /nologo /target:exe /codepage:65001 @references ('/reference:' + (Join-Path $product 'ClickDeck.exe')) ('/out:' + $runner) (Join-Path $PSScriptRoot 'TestRunner.cs')
if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败。' }
Copy-Item -LiteralPath (Join-Path $product 'ClickDeck.exe') -Destination $WorkDirectory
if (-not $PreviewPath) { $PreviewPath = Join-Path $WorkDirectory 'preview.png' }
& $runner $target $WorkDirectory $PreviewPath
if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
