param([string]$OutputFolder = '纸盒包装创意工作台',[switch]$Qa)
$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$version = ([IO.File]::ReadAllText((Join-Path $sourceDir 'version.txt'))).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'version.txt 必须是语义化版本，如 1.1.0' }
if ($OutputFolder -eq '纸盒包装创意工作台') { $OutputFolder = '纸盒包装创意工作台-V' + $version }
& (Join-Path $sourceDir 'verify-knowledge.ps1')
$outputDir = Join-Path (Join-Path (Split-Path $sourceDir) 'output') $OutputFolder
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$argsFile = Join-Path $sourceDir 'build.rsp'
$versionInfo = Join-Path $outputDir 'VersionInfo.g.cs'
$assemblyVersion = $version + '.0'
[IO.File]::WriteAllText($versionInfo, ('using System.Reflection;' + "`n" + '[assembly: AssemblyTitle("纸盒包装创意工作台")]' + "`n" + '[assembly: AssemblyProduct("纸盒包装创意工作台")]' + "`n" + '[assembly: AssemblyVersion("' + $assemblyVersion + '")]' + "`n" + '[assembly: AssemblyFileVersion("' + $assemblyVersion + '")]' + "`n" + '[assembly: AssemblyInformationalVersion("' + $version + '")]'), [Text.Encoding]::UTF8)
$lines = @('/nologo', '/target:winexe', '/optimize+', '/utf8output', ('/out:"' + (Join-Path $outputDir '纸盒包装创意工作台.exe') + '"'))
$lines += '"' + $versionInfo + '"'
$lines += '/win32icon:"' + (Join-Path $sourceDir 'resources/studio-logo.ico') + '"'
foreach ($name in @('System.IO.Compression.dll','System.Drawing.dll','System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Net.Http.dll','System.Security.dll','Microsoft.CSharp.dll')) {
    $lines += '/reference:"' + (Join-Path $framework $name) + '"'
}
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Printing.dll','ReachFramework.dll')) {
    $lines += '/reference:"' + (Join-Path $framework ('WPF\' + $name)) + '"'
}
$lines += '/resource:"' + (Join-Path $sourceDir 'Workspace.xaml') + '",Workspace.xaml'
$lines += '/resource:"' + (Join-Path $sourceDir 'skills/packaging-designer/SKILL.md') + '",packaging-designer.SKILL.md'
foreach ($resource in Get-ChildItem -LiteralPath (Join-Path $sourceDir 'resources') -File) {
    $lines += '/resource:"' + $resource.FullName + '",' + $resource.Name
}
foreach ($name in @('CreativeStudio.cs','GenerationTask.cs','LoadingRunner.cs','RunnerArt.cs','SkatingMascot.cs','DesignModels.cs','DesignStrategy.cs','ApiSecretStore.cs','PackagingConstraints.cs','AssemblyGeometry.cs','AssemblyResponse.cs','AssemblyGuide.cs','CalculatedSketch.cs','ProportionGuide.cs','DesignService.cs','TextPicture.cs','ChatStreamReader.cs','DesignMethod.cs','SketchOutput.cs','ReliableConcepts.cs','DesignWorkflow.cs','KnowledgeCache.cs','ImageInspection.cs','SamplePackage.cs','PackboxExport.cs','CitationGuard.cs','CitationCatalog.cs','AgentOrchestration.cs','PythonGeometryTool.cs','ReferenceDiagnostics.cs')) { $lines += '"' + (Join-Path $sourceDir $name) + '"' }
if ($Qa) { $lines += '"' + (Join-Path $sourceDir 'SketchQualityChecks.cs') + '"'; $lines += '"' + (Join-Path $sourceDir 'WorkflowFixtures.cs') + '"'; $lines += '/define:QA'; $lines += '"' + (Join-Path $sourceDir 'DesktopQa.cs') + '"' }
$lines += '"' + (Join-Path $sourceDir 'ReferenceDesign.cs') + '"'
[IO.File]::WriteAllLines($argsFile, $lines, [Text.Encoding]::UTF8)
& (Join-Path $framework 'csc.exe') ('@' + $argsFile)
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath (Join-Path $sourceDir '使用说明.txt') -Destination $outputDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'python-tools') -Destination $outputDir -Recurse -Force
$diagnosticDir = Join-Path $outputDir 'diagnostics'
New-Item -ItemType Directory -Force -Path $diagnosticDir | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir 'diagnose_knowledge.py') -Destination $diagnosticDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'stable_source_ids.py') -Destination $diagnosticDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'resources') -Destination $diagnosticDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'resources/LUCIDE-LICENSE.txt') -Destination $outputDir -Force
$packboxOutput = Join-Path $outputDir 'packbox'
New-Item -ItemType Directory -Force -Path (Join-Path $packboxOutput 'runtime') | Out-Null
foreach ($name in @('packbox-cli.cjs','templates.json','PACKBOX-LICENSE.txt','UPSTREAM.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceDir ('packbox/' + $name)) -Destination $packboxOutput -Force
}
foreach ($name in @('node.exe','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $sourceDir ('packbox/runtime/' + $name)) -Destination (Join-Path $packboxOutput 'runtime') -Force
}
Get-Item -LiteralPath (Join-Path $outputDir '纸盒包装创意工作台.exe') | Select-Object FullName,Length
