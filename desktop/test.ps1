$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot)
try {
    $referenceSource = 'desktop\ReferenceDesign.cs'
    & (Join-Path $PSScriptRoot 'verify-knowledge.ps1')
    New-Item -ItemType Directory -Force -Path 'tmp' | Out-Null
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /utf8output /define:TEST /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Security.dll /r:Microsoft.CSharp.dll /out:tmp\design-tests.exe desktop\DesignModels.cs $referenceSource desktop\DesignStrategy.cs desktop\GenerationTask.cs desktop\ApiSecretStore.cs desktop\PackagingConstraints.cs desktop\AssemblyGeometry.cs desktop\AssemblyResponse.cs desktop\AssemblyResponseChecks.cs /resource:desktop\resources\packaging-design-method.md,packaging-design-method.md /resource:desktop\skills\packaging-designer\SKILL.md,packaging-designer.SKILL.md desktop\SketchOutput.cs desktop\CalculatedSketch.cs desktop\DesignMethod.cs desktop\CitationGuard.cs desktop\CitationGuardChecks.cs desktop\CitationCatalog.cs desktop\CitationCatalogChecks.cs desktop\AgentOrchestration.cs desktop\PythonGeometryTool.cs desktop\AgentOrchestrationChecks.cs desktop\ReferenceDiagnostics.cs desktop\ReliableConcepts.cs desktop\DesignWorkflow.cs desktop\DesignService.cs desktop\TextPicture.cs desktop\ChatStreamReader.cs desktop\ChatStreamChecks.cs desktop\WorkflowFixtures.cs desktop\KnowledgeCache.cs desktop\KnowledgeCacheChecks.cs desktop\RequirementQualityChecks.cs desktop\GeometryQualityChecks.cs desktop\ImageRecoveryChecks.cs desktop\ImageInspection.cs desktop\ImageInspectionChecks.cs desktop\SamplePackage.cs desktop\SamplePackageChecks.cs desktop\DesignTests.cs
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
    & tmp\design-tests.exe
    if ($LASTEXITCODE -ne 0) { throw '测试失败' }
} finally { Pop-Location }
