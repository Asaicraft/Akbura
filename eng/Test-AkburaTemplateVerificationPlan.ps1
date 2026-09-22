param()

$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "AkburaTemplateVerificationPlan.psm1") -Force

function Assert-Condition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [string] $Message
    )

    try {
        & $Action
    }
    catch {
        return
    }
    throw $Message
}

function Copy-JsonObject {
    param([Parameter(Mandatory)] $Value)

    return $Value | ConvertTo-Json -Depth 12 | ConvertFrom-Json -Depth 12
}

$catalog = @(Get-AkburaTemplateCaseCatalog)
Assert-Condition ($catalog.Count -eq 144) (
    "Template catalog contains $($catalog.Count) cases, expected 144.")
Assert-Condition (@($catalog | Where-Object Kind -eq "mvvm").Count -eq 24) (
    "Template catalog must contain 24 MVVM cases.")
Assert-Condition (@($catalog | Where-Object Kind -eq "xplat").Count -eq 120) (
    "Template catalog must contain 120 xplat cases.")
Assert-Condition (@($catalog.Id | Sort-Object -Unique).Count -eq 144) (
    "Template catalog case IDs are not unique.")

$releaseContext = [pscustomobject] [ordered] @{
    SourceCommit = "template-plan-tests"
    SourceDirty = $false
    AkburaVersion = "12.0.4-test"
    AvaloniaVersion = "12.0.4"
    Packages = @(
        [pscustomobject] [ordered] @{
            Name = "Akbura"
            Sha256 = "runtime-hash"
        },
        [pscustomobject] [ordered] @{
            Name = "Akbura.Diagnostics"
            Sha256 = "diagnostics-hash"
        },
        [pscustomobject] [ordered] @{
            Name = "Akbura.Templates"
            Sha256 = "templates-hash"
        })
    WorkflowRunId = "test-run"
    WorkflowRunAttempt = "1"
}

$deterministicIndexProvider = {
    param([int] $UpperExclusive)
    return $UpperExclusive - 1
}
$sampledArguments = @{
    Mode = "Sampled"
    Catalog = $catalog
    ReleaseContext = $releaseContext
    RandomIndexProvider = $deterministicIndexProvider
}
$sampledPlan = New-AkburaTemplateVerificationPlan @sampledArguments
[void] (Test-AkburaTemplateVerificationPlan -Plan $sampledPlan -Catalog $catalog -ExpectedReleaseContext $releaseContext)

$sampledCases = @($sampledPlan.Cases)
$sampledExecutions = @($sampledPlan.Executions)
Assert-Condition ($sampledCases.Count -eq 20) (
    "Sampled plan must contain 20 unique cases.")
Assert-Condition ($sampledExecutions.Count -eq 24) (
    "Sampled plan must contain 24 unique executions.")
Assert-Condition (@($sampledExecutions | Where-Object Configuration -eq "Debug").Count -eq 20) (
    "Sampled plan must contain 20 Debug executions.")
Assert-Condition (@($sampledExecutions | Where-Object Configuration -eq "Release").Count -eq 4) (
    "Sampled plan must contain four Release executions.")

$smokeExecutions = @(Get-AkburaSmokeExecutions -Catalog $catalog)
$smokeCaseIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($execution in $smokeExecutions) {
    [void] $smokeCaseIds.Add($execution.CaseId)
}
Assert-Condition (@($sampledCases | Where-Object {
    !$smokeCaseIds.Contains($_.Id)
}).Count -gt 0) (
    "Sampled selection was unexpectedly restricted to the legacy smoke set.")

$smokePlan = New-AkburaTemplateVerificationPlan -Mode Smoke -Catalog $catalog -ReleaseContext $releaseContext
[void] (Test-AkburaTemplateVerificationPlan -Plan $smokePlan -Catalog $catalog -ExpectedReleaseContext $releaseContext)
Assert-Condition (
    @($smokePlan.StructuralCaseIds).Count -eq 144 -and
    @($smokePlan.Executions).Count -eq 24) (
    "Smoke plan no longer preserves its 144/24 contract.")

$tamperedSmokePlan = $smokePlan | ConvertTo-Json -Depth 12 | ConvertFrom-Json -Depth 12
$tamperedSmokePlan.Executions[0].CaseId = $catalog[-1].Id
Assert-Throws {
    Test-AkburaTemplateVerificationPlan `
        -Plan $tamperedSmokePlan `
        -Catalog $catalog `
        -ExpectedReleaseContext $releaseContext
} "fixed smoke contract"

$fullPlan = New-AkburaTemplateVerificationPlan -Mode Full -Catalog $catalog -ReleaseContext $releaseContext
[void] (Test-AkburaTemplateVerificationPlan -Plan $fullPlan -Catalog $catalog -ExpectedReleaseContext $releaseContext)
Assert-Condition (
    @($fullPlan.StructuralCaseIds).Count -eq 144 -and
    @($fullPlan.Executions).Count -eq 288) (
    "Full plan must contain 144 structural cases and 288 executions.")

$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()) (
    "AkburaTemplatePlanTests-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $planPath = Join-Path $temporaryRoot "plan.json"
    [void] (Write-AkburaTemplateVerificationPlan -Plan $sampledPlan -Path $planPath)
    $replayArguments = @{
        Mode = "Sampled"
        Catalog = $catalog
        ReleaseContext = $releaseContext
        SelectionManifest = $planPath
        RandomIndexProvider = {
            throw "Replay must not invoke the random index provider."
        }
    }
    $replayedPlan = Resolve-AkburaTemplateVerificationPlan @replayArguments
    Assert-Condition (
        [string]::Join([Environment]::NewLine, @($replayedPlan.StructuralCaseIds)) -eq
        [string]::Join([Environment]::NewLine, @($sampledPlan.StructuralCaseIds))) (
        "Replay changed structural case order.")
    $replayedExecutions = @(
        $replayedPlan.Executions |
            ForEach-Object { "$($_.CaseId)|$($_.Configuration)" })
    $sampledExecutionKeys = @(
        $sampledPlan.Executions |
            ForEach-Object { "$($_.CaseId)|$($_.Configuration)" })
    Assert-Condition (
        [string]::Join([Environment]::NewLine, $replayedExecutions) -eq
        [string]::Join([Environment]::NewLine, $sampledExecutionKeys)) (
        "Replay changed execution fields or order.")

    $invalidHashPlan = Copy-JsonObject $sampledPlan
    $invalidHashPlan.Catalog.Sha256 = "invalid"
    $invalidHashPath = Join-Path $temporaryRoot "invalid-hash.json"
    [void] (Write-AkburaTemplateVerificationPlan -Plan $invalidHashPlan -Path $invalidHashPath)
    Assert-Throws {
        Read-AkburaTemplateVerificationPlan -Path $invalidHashPath -Catalog $catalog -ExpectedReleaseContext $releaseContext
    } "A plan with an incompatible catalog hash was accepted."

    $duplicatePlan = Copy-JsonObject $sampledPlan
    $duplicatePlan.Executions = @($duplicatePlan.Executions) + @(
        $duplicatePlan.Executions[0])
    $duplicatePath = Join-Path $temporaryRoot "duplicate.json"
    [void] (Write-AkburaTemplateVerificationPlan -Plan $duplicatePlan -Path $duplicatePath)
    Assert-Throws {
        Read-AkburaTemplateVerificationPlan -Path $duplicatePath -Catalog $catalog -ExpectedReleaseContext $releaseContext
    } "A plan with duplicate executions was accepted."

    $incompatibleContext = Copy-JsonObject $releaseContext
    $incompatibleContext.AkburaVersion = "different-version"
    Assert-Throws {
        Read-AkburaTemplateVerificationPlan -Path $planPath -Catalog $catalog -ExpectedReleaseContext $incompatibleContext
    } "A plan for different package versions was accepted."
}
finally {
    if ([IO.Directory]::Exists($temporaryRoot)) {
        [IO.Directory]::Delete($temporaryRoot, $true)
    }
}

$productionPlan = New-AkburaTemplateVerificationPlan -Mode Sampled -Catalog $catalog -ReleaseContext $releaseContext
[void] (Test-AkburaTemplateVerificationPlan -Plan $productionPlan -Catalog $catalog -ExpectedReleaseContext $releaseContext)

Write-Host (
    "Verified template planner: 144 catalog cases, " +
    "20/24 Sampled, 144/24 Smoke, and 144/288 Full.")
