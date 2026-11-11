param(
    [switch]$SkipBuild,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$stage0Root = Split-Path -Parent $PSScriptRoot
$stage0Projects = @('Travela.ReservationTests', 'Travela.RouteTests', 'Travela.ReportingTests')

function Invoke-Stage0Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Aşama 0 komutu başarısız oldu (çıkış $LASTEXITCODE): dotnet $($Arguments -join ' ')"
    }
}

Push-Location $stage0Root
try {
    if (-not $SkipBuild) {
        Invoke-Stage0Dotnet -Arguments @(
            'build', 'backend/Travela.API.csproj', '--configuration', $Configuration, '--no-restore'
        )
        foreach ($project in $stage0Projects) {
            Invoke-Stage0Dotnet -Arguments @('build', "tests/$project", '--configuration', $Configuration, '--no-restore')
        }
    }
    Invoke-Stage0Dotnet -Arguments @(
        'run', '--project', 'tests/Travela.ReservationTests', '--configuration', $Configuration,
        '--no-build', '--no-restore', '--', '--test-db-unit'
    )
    foreach ($project in $stage0Projects) {
        Write-Host "Aşama 0: $project"
        Invoke-Stage0Dotnet -Arguments @(
            'run', '--project', "tests/$project", '--configuration', $Configuration, '--no-build', '--no-restore'
        )
    }
    Write-Host 'Aşama 0: üç SQL entegrasyon paketi başarılı; Aşama 1 başlatılmadı.'
}
finally {
    Pop-Location
}
