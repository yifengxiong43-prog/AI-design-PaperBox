$ErrorActionPreference = 'Stop'
$resourceDir = Join-Path $PSScriptRoot 'resources'
$catalogue = Get-Content -LiteralPath (Join-Path $resourceDir 'knowledge-analysis.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($inputHash in $catalogue.InputHashes.PSObject.Properties) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $resourceDir $inputHash.Name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $inputHash.Value) { throw ('资料分析已过期：' + $inputHash.Name + '。请运行 python desktop/build_knowledge_analysis.py 重建。') }
}
foreach ($picture in $catalogue.Images) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $resourceDir $picture.Resource) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $picture.Sha256) { throw ('图片分析已过期：' + $picture.Resource + '。请重新导入图片并重建资料分析。') }
}
