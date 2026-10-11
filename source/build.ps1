$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.8 的 C# 编译器。' }
$destination = Split-Path -Parent $PSScriptRoot
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/out:' + (Join-Path $destination 'ClickDeck.exe')))
$icon = Join-Path $destination 'ClickDeck.ico'
if (Test-Path -LiteralPath $icon) { $arguments += '/win32icon:' + $icon }
$arguments += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output (Join-Path $destination 'ClickDeck.exe')
