param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$previewRoot = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $previewRoot '..\..')).Path
$runtime = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputDirectory = Join-Path $previewRoot 'bin'
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'ViewerWorkspacePreview.exe'
& (Join-Path $runtime 'csc.exe') /nologo /target:exe "/out:$output" `
    "/r:$runtime\WPF\PresentationFramework.dll" "/r:$runtime\WPF\PresentationCore.dll" `
    "/r:$runtime\WPF\WindowsBase.dll" "/r:$runtime\System.Xaml.dll" (Join-Path $previewRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed.' }
$assemblyDirectory = Join-Path $repository 'src\ArrayImageViewer\bin\Debug'
if ($Verify) { & $output $assemblyDirectory --verify } else { & $output $assemblyDirectory }
exit $LASTEXITCODE
