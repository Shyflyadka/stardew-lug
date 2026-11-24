$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler is required.' }
$references = 'System.Drawing', 'System.Windows.Forms', 'System.Web', 'System.Web.Extensions', 'System.Net.Http',
    'System.IO.Compression', 'System.IO.Compression.FileSystem', 'System.Security'
$compilerReferences = $references | ForEach-Object { '/reference:' + (Join-Path $framework ($_ + '.dll')) }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$options = @('/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4')
$icon = Join-Path $PSScriptRoot 'lug.ico'
if (Test-Path -LiteralPath $icon) { $options += '/win32icon:' + $icon }
$output = Join-Path $PSScriptRoot 'StardewLug.exe'
& $compiler @options "/out:$output" @compilerReferences @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output $output
