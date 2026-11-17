param([Parameter(Mandatory=$true)][string]$ApplicationPath,[string]$OutputFolder='tmp/qa-v089')
$ErrorActionPreference='Stop'
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$workspace=Split-Path $PSScriptRoot
$check=Join-Path $workspace 'tmp/packbox-desktop-checks.exe'
$compilerArgs=@('/nologo','/utf8output',('/out:'+$check))
foreach($name in @('System.dll','System.Core.dll','System.Xaml.dll','System.IO.Compression.dll')) { $compilerArgs+=('/reference:'+(Join-Path $framework $name)) }
foreach($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $compilerArgs+=('/reference:'+(Join-Path $framework ('WPF/'+$name))) }
$compilerArgs+=(Join-Path $PSScriptRoot 'PackboxDesktopChecks.cs')
& (Join-Path $framework 'csc.exe') $compilerArgs
if($LASTEXITCODE -ne 0){throw 'Packbox integration checks failed to compile'}
& $check (Resolve-Path -LiteralPath $ApplicationPath).Path ([IO.Path]::GetFullPath($OutputFolder))
if($LASTEXITCODE -ne 0){throw 'Packbox integration checks failed'}
