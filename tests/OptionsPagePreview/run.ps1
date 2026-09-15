param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$previewOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $previewOutput | Out-Null
$previewExe = Join-Path $previewOutput 'OptionsPagePreview.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$previewExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $PSScriptRoot 'TemplateOptionsChecks.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previewAssembly = Join-Path $PSScriptRoot "..\..\src\ArrayImageViewer\bin\$Configuration"
& $previewExe $previewAssembly
exit $LASTEXITCODE
