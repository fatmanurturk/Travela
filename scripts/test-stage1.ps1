param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$segmentRoot = Split-Path -Parent $PSScriptRoot
Push-Location $segmentRoot
try {
    & "$PSScriptRoot/test-stage0.ps1" -Configuration $Configuration
    & dotnet run --project tests/Travela.ReservationTests --configuration $Configuration --no-build --no-restore -- --segment-tests
    if ($LASTEXITCODE -ne 0) { throw 'Segment SQL testleri başarısız.' }
    & dotnet ef migrations has-pending-model-changes --project backend --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'EF modeli ve migration taslağı eşleşmiyor.' }
    Write-Host 'Aşama 1 SQL doğrulaması başarılı; ana veritabanına geçiş ayrı onay gerektirir.'
}
finally { Pop-Location }
