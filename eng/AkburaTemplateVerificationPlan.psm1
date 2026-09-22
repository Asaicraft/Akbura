Set-StrictMode -Version Latest

$script:SchemaVersion = 1
$script:SampledAlgorithmVersion = "stratified-csprng-v1"
$script:StaticAlgorithmVersion = "static-matrix-v1"
$script:Kinds = @("mvvm", "xplat")
$script:Toolkits = @("CommunityToolkit", "ReactiveUI")
$script:DependencyInjectionOptions = @(
    "None",
    "Microsoft.Extensions.DependencyInjection",
    "Splat.Locator")
$script:PageTypes = @(
    "None",
    "ContentPage",
    "TabbedPage",
    "DrawerPage",
    "NavigationPage")

function Assert-AkburaTemplatePlanCondition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Get-AkburaTemplateCaseId {
    param([Parameter(Mandatory)] $Case)

    $cpm = ([bool] $Case.Cpm).ToString().ToLowerInvariant()
    $removeViewLocator =
        ([bool] $Case.RemoveViewLocator).ToString().ToLowerInvariant()
    return "Kind=$($Case.Kind);" +
        "Toolkit=$($Case.Toolkit);" +
        "DependencyInjection=$($Case.DependencyInjection);" +
        "Cpm=$cpm;" +
        "RemoveViewLocator=$removeViewLocator;" +
        "PageType=$($Case.PageType)"
}

function New-AkburaTemplateCase {
    param(
        [Parameter(Mandatory)] [string] $Kind,
        [Parameter(Mandatory)] [string] $Toolkit,
        [Parameter(Mandatory)] [string] $DependencyInjection,
        [Parameter(Mandatory)] [bool] $Cpm,
        [Parameter(Mandatory)] [bool] $RemoveViewLocator,
        [Parameter(Mandatory)] [string] $PageType
    )

    $case = [pscustomobject] [ordered] @{
        Kind = $Kind
        Toolkit = $Toolkit
        DependencyInjection = $DependencyInjection
        Cpm = $Cpm
        RemoveViewLocator = $RemoveViewLocator
        PageType = $PageType
    }
    $case | Add-Member -NotePropertyName Id `
        -NotePropertyValue (Get-AkburaTemplateCaseId $case)
    return $case
}

function Get-AkburaTemplateCaseCatalog {
    $cases = [Collections.Generic.List[object]]::new()

    foreach ($kind in $script:Kinds) {
        $pages = if ($kind -eq "mvvm") { @("None") } else { $script:PageTypes }
        foreach ($toolkit in $script:Toolkits) {
            foreach ($dependencyInjection in $script:DependencyInjectionOptions) {
                foreach ($cpm in @($false, $true)) {
                    foreach ($removeViewLocator in @($false, $true)) {
                        foreach ($pageType in $pages) {
                            $cases.Add((New-AkburaTemplateCase `
                                -Kind $kind `
                                -Toolkit $toolkit `
                                -DependencyInjection $dependencyInjection `
                                -Cpm $cpm `
                                -RemoveViewLocator $removeViewLocator `
                                -PageType $pageType))
                        }
                    }
                }
            }
        }
    }

    return @($cases | Sort-Object Id)
}

function Get-AkburaTemplateCatalogHash {
    param([Parameter(Mandatory)] [object[]] $Catalog)

    [string[]] $ids = @($Catalog | ForEach-Object { [string] $_.Id })
    [Array]::Sort($ids, [StringComparer]::Ordinal)
    $content = [string]::Join("`n", $ids)
    $bytes = [Text.Encoding]::UTF8.GetBytes($content)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-AkburaRandomIndex {
    param(
        [Parameter(Mandatory)] [int] $UpperExclusive,
        [scriptblock] $RandomIndexProvider
    )

    Assert-AkburaTemplatePlanCondition ($UpperExclusive -gt 0) (
        "Random selection requires a positive upper bound.")
    $index = if ($null -eq $RandomIndexProvider) {
        [Security.Cryptography.RandomNumberGenerator]::GetInt32(
            0,
            $UpperExclusive)
    }
    else {
        & $RandomIndexProvider $UpperExclusive
    }
    Assert-AkburaTemplatePlanCondition (
        $index -is [int] -and $index -ge 0 -and
        $index -lt $UpperExclusive) (
        "Random index provider returned invalid index '$index' " +
        "for upper bound $UpperExclusive.")
    return [int] $index
}

function Get-AkburaShuffledItems {
    param(
        [Parameter(Mandatory)] [object[]] $Items,
        [scriptblock] $RandomIndexProvider
    )

    $result = [Collections.Generic.List[object]]::new()
    foreach ($item in $Items) {
        $result.Add($item)
    }
    for ($index = $result.Count - 1; $index -gt 0; $index--) {
        $swapIndex = Get-AkburaRandomIndex `
            -UpperExclusive ($index + 1) `
            -RandomIndexProvider $RandomIndexProvider
        $temporary = $result[$index]
        $result[$index] = $result[$swapIndex]
        $result[$swapIndex] = $temporary
    }
    return @($result)
}

function Select-AkburaRandomItem {
    param(
        [Parameter(Mandatory)] [object[]] $Items,
        [scriptblock] $RandomIndexProvider
    )

    Assert-AkburaTemplatePlanCondition ($Items.Count -gt 0) (
        "The template planner could not satisfy a required sampling stratum.")
    $index = Get-AkburaRandomIndex `
        -UpperExclusive $Items.Count `
        -RandomIndexProvider $RandomIndexProvider
    return $Items[$index]
}

function Get-AkburaCaseLookup {
    param([Parameter(Mandatory)] [object[]] $Catalog)

    $lookup = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    foreach ($case in $Catalog) {
        Assert-AkburaTemplatePlanCondition (!$lookup.ContainsKey($case.Id)) (
            "Template catalog contains duplicate case ID '$($case.Id)'.")
        $lookup.Add($case.Id, $case)
    }
    return $lookup
}

function Find-AkburaTemplateCase {
    param(
        [Parameter(Mandatory)] [object[]] $Catalog,
        [Parameter(Mandatory)] [string] $Kind,
        [Parameter(Mandatory)] [string] $Toolkit,
        [Parameter(Mandatory)] [string] $DependencyInjection,
        [Parameter(Mandatory)] [bool] $Cpm,
        [Parameter(Mandatory)] [bool] $RemoveViewLocator,
        [Parameter(Mandatory)] [string] $PageType
    )

    $matches = @($Catalog | Where-Object {
        $_.Kind -eq $Kind -and
        $_.Toolkit -eq $Toolkit -and
        $_.DependencyInjection -eq $DependencyInjection -and
        $_.Cpm -eq $Cpm -and
        $_.RemoveViewLocator -eq $RemoveViewLocator -and
        $_.PageType -eq $PageType
    })
    Assert-AkburaTemplatePlanCondition ($matches.Count -eq 1) (
        "Expected one catalog case for $Kind/$Toolkit/" +
        "$DependencyInjection/$Cpm/$RemoveViewLocator/$PageType, " +
        "found $($matches.Count).")
    return $matches[0]
}

function New-AkburaExecution {
    param(
        [Parameter(Mandatory)] [string] $CaseId,
        [Parameter(Mandatory)] [ValidateSet("Debug", "Release")]
        [string] $Configuration
    )

    return [pscustomobject] [ordered] @{
        CaseId = $CaseId
        Configuration = $Configuration
    }
}

function Get-AkburaSmokeExecutions {
    param([Parameter(Mandatory)] [object[]] $Catalog)

    $descriptors = @(
        @("mvvm", "CommunityToolkit", "None", $false, $false, "None", @("Debug", "Release")),
        @("mvvm", "CommunityToolkit", "Microsoft.Extensions.DependencyInjection", $true, $true, "None", @("Debug")),
        @("mvvm", "CommunityToolkit", "Splat.Locator", $false, $true, "None", @("Debug")),
        @("mvvm", "ReactiveUI", "None", $true, $false, "None", @("Debug")),
        @("mvvm", "ReactiveUI", "Microsoft.Extensions.DependencyInjection", $false, $true, "None", @("Debug")),
        @("mvvm", "ReactiveUI", "Splat.Locator", $true, $false, "None", @("Debug", "Release")),
        @("xplat", "CommunityToolkit", "None", $true, $false, "None", @("Debug", "Release")),
        @("xplat", "CommunityToolkit", "Microsoft.Extensions.DependencyInjection", $false, $true, "None", @("Debug")),
        @("xplat", "CommunityToolkit", "Splat.Locator", $true, $true, "None", @("Debug")),
        @("xplat", "ReactiveUI", "None", $false, $false, "None", @("Debug")),
        @("xplat", "ReactiveUI", "Microsoft.Extensions.DependencyInjection", $true, $true, "None", @("Debug")),
        @("xplat", "ReactiveUI", "Splat.Locator", $false, $false, "None", @("Debug", "Release")),
        @("xplat", "CommunityToolkit", "None", $false, $false, "ContentPage", @("Debug")),
        @("xplat", "ReactiveUI", "None", $true, $true, "ContentPage", @("Debug")),
        @("xplat", "CommunityToolkit", "None", $true, $false, "TabbedPage", @("Debug")),
        @("xplat", "ReactiveUI", "None", $false, $true, "TabbedPage", @("Debug")),
        @("xplat", "CommunityToolkit", "None", $false, $true, "DrawerPage", @("Debug")),
        @("xplat", "ReactiveUI", "None", $true, $false, "DrawerPage", @("Debug")),
        @("xplat", "CommunityToolkit", "None", $true, $true, "NavigationPage", @("Debug")),
        @("xplat", "ReactiveUI", "None", $false, $true, "NavigationPage", @("Debug"))
    )
    $executions = [Collections.Generic.List[object]]::new()
    foreach ($descriptor in $descriptors) {
        $case = Find-AkburaTemplateCase `
            -Catalog $Catalog `
            -Kind $descriptor[0] `
            -Toolkit $descriptor[1] `
            -DependencyInjection $descriptor[2] `
            -Cpm $descriptor[3] `
            -RemoveViewLocator $descriptor[4] `
            -PageType $descriptor[5]
        foreach ($configuration in $descriptor[6]) {
            $executions.Add((New-AkburaExecution `
                -CaseId $case.Id `
                -Configuration $configuration))
        }
    }
    return @($executions)
}

function Get-AkburaSampledSelection {
    param(
        [Parameter(Mandatory)] [object[]] $Catalog,
        [scriptblock] $RandomIndexProvider
    )

    $selected = [Collections.Generic.List[object]]::new()
    $mvvmPairs = [Collections.Generic.List[object]]::new()
    foreach ($toolkit in $script:Toolkits) {
        foreach ($dependencyInjection in $script:DependencyInjectionOptions) {
            $mvvmPairs.Add([pscustomobject] @{
                Toolkit = $toolkit
                DependencyInjection = $dependencyInjection
            })
        }
    }
    $mvvmPairs = @(Get-AkburaShuffledItems `
        -Items @($mvvmPairs) `
        -RandomIndexProvider $RandomIndexProvider)
    $cpmFlags = @(Get-AkburaShuffledItems `
        -Items @($false, $false, $false, $true, $true, $true) `
        -RandomIndexProvider $RandomIndexProvider)
    $locatorFlags = @(Get-AkburaShuffledItems `
        -Items @($false, $false, $false, $true, $true, $true) `
        -RandomIndexProvider $RandomIndexProvider)
    for ($index = 0; $index -lt $mvvmPairs.Count; $index++) {
        $pair = $mvvmPairs[$index]
        $selected.Add((Find-AkburaTemplateCase `
            -Catalog $Catalog `
            -Kind "mvvm" `
            -Toolkit $pair.Toolkit `
            -DependencyInjection $pair.DependencyInjection `
            -Cpm $cpmFlags[$index] `
            -RemoveViewLocator $locatorFlags[$index] `
            -PageType "None"))
    }

    foreach ($toolkit in $script:Toolkits) {
        $pages = @(Get-AkburaShuffledItems `
            -Items $script:PageTypes `
            -RandomIndexProvider $RandomIndexProvider)
        $dependencyInjectionAssignments =
            [Collections.Generic.List[object]]::new()
        foreach ($dependencyInjection in $script:DependencyInjectionOptions) {
            $dependencyInjectionAssignments.Add($dependencyInjection)
        }
        for ($index = 0; $index -lt 2; $index++) {
            $dependencyInjectionAssignments.Add((Select-AkburaRandomItem `
                -Items $script:DependencyInjectionOptions `
                -RandomIndexProvider $RandomIndexProvider))
        }
        $dependencyInjectionAssignments = @(Get-AkburaShuffledItems `
            -Items @($dependencyInjectionAssignments) `
            -RandomIndexProvider $RandomIndexProvider)

        for ($index = 0; $index -lt $script:PageTypes.Count; $index++) {
            $candidates = @($Catalog | Where-Object {
                $_.Kind -eq "xplat" -and
                $_.Toolkit -eq $toolkit -and
                $_.DependencyInjection -eq
                    $dependencyInjectionAssignments[$index] -and
                $_.PageType -eq $pages[$index]
            })
            $selected.Add((Select-AkburaRandomItem `
                -Items $candidates `
                -RandomIndexProvider $RandomIndexProvider))
        }

        $selectedIds = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($case in $selected) {
            [void] $selectedIds.Add($case.Id)
        }
        $remaining = @($Catalog | Where-Object {
            $_.Kind -eq "xplat" -and
            $_.Toolkit -eq $toolkit -and
            !$selectedIds.Contains($_.Id)
        })
        $remaining = @(Get-AkburaShuffledItems `
            -Items $remaining `
            -RandomIndexProvider $RandomIndexProvider)
        Assert-AkburaTemplatePlanCondition ($remaining.Count -ge 2) (
            "The template planner cannot select two additional xplat cases " +
            "for $toolkit.")
        $selected.Add($remaining[0])
        $selected.Add($remaining[1])
    }

    $selected = @(Get-AkburaShuffledItems `
        -Items @($selected) `
        -RandomIndexProvider $RandomIndexProvider)
    $selectedIds = @($selected | ForEach-Object { $_.Id })
    Assert-AkburaTemplatePlanCondition (
        @($selectedIds | Sort-Object -Unique).Count -eq 20) (
        "Sampled template plan did not produce 20 unique case IDs.")

    $executions = [Collections.Generic.List[object]]::new()
    foreach ($case in $selected) {
        $executions.Add((New-AkburaExecution `
            -CaseId $case.Id `
            -Configuration "Debug"))
    }
    foreach ($kind in $script:Kinds) {
        foreach ($toolkit in $script:Toolkits) {
            $releaseCandidates = @($selected | Where-Object {
                $_.Kind -eq $kind -and $_.Toolkit -eq $toolkit
            })
            $releaseCase = Select-AkburaRandomItem `
                -Items $releaseCandidates `
                -RandomIndexProvider $RandomIndexProvider
            $executions.Add((New-AkburaExecution `
                -CaseId $releaseCase.Id `
                -Configuration "Release"))
        }
    }

    return [pscustomobject] @{
        StructuralCases = $selected
        Executions = @($executions)
    }
}

function New-AkburaTemplateVerificationPlan {
    param(
        [Parameter(Mandatory)] [ValidateSet("Sampled", "Smoke", "Full")]
        [string] $Mode,
        [Parameter(Mandatory)] [object[]] $Catalog,
        [Parameter(Mandatory)] $ReleaseContext,
        [scriptblock] $RandomIndexProvider
    )

    Assert-AkburaTemplatePlanCondition ($Catalog.Count -eq 144) (
        "Template catalog contains $($Catalog.Count) cases; expected 144.")
    $selection = switch ($Mode) {
        "Sampled" {
            Get-AkburaSampledSelection `
                -Catalog $Catalog `
                -RandomIndexProvider $RandomIndexProvider
        }
        "Smoke" {
            [pscustomobject] @{
                StructuralCases = $Catalog
                Executions = @(Get-AkburaSmokeExecutions -Catalog $Catalog)
            }
        }
        "Full" {
            $executions = [Collections.Generic.List[object]]::new()
            foreach ($case in $Catalog) {
                foreach ($configuration in @("Debug", "Release")) {
                    $executions.Add((New-AkburaExecution `
                        -CaseId $case.Id `
                        -Configuration $configuration))
                }
            }
            [pscustomobject] @{
                StructuralCases = $Catalog
                Executions = @($executions)
            }
        }
    }
    $algorithmVersion = if ($Mode -eq "Sampled") {
        $script:SampledAlgorithmVersion
    }
    else {
        $script:StaticAlgorithmVersion
    }

    return [pscustomobject] [ordered] @{
        SchemaVersion = $script:SchemaVersion
        AlgorithmVersion = $algorithmVersion
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        Mode = $Mode
        Source = $ReleaseContext
        Catalog = [pscustomobject] [ordered] @{
            Count = $Catalog.Count
            Sha256 = Get-AkburaTemplateCatalogHash -Catalog $Catalog
        }
        Cases = @($selection.StructuralCases)
        StructuralCaseIds = @(
            $selection.StructuralCases |
                ForEach-Object { $_.Id })
        Executions = @($selection.Executions)
    }
}

function Test-AkburaTemplateVerificationPlan {
    param(
        [Parameter(Mandatory)] $Plan,
        [Parameter(Mandatory)] [object[]] $Catalog,
        $ExpectedReleaseContext
    )

    Assert-AkburaTemplatePlanCondition (
        [int] $Plan.SchemaVersion -eq $script:SchemaVersion) (
        "Unsupported template verification plan schema " +
        "'$($Plan.SchemaVersion)'.")
    Assert-AkburaTemplatePlanCondition (
        $Plan.Mode -in @("Sampled", "Smoke", "Full")) (
        "Unsupported template verification mode '$($Plan.Mode)'.")
    $expectedAlgorithmVersion = if ($Plan.Mode -eq "Sampled") {
        $script:SampledAlgorithmVersion
    }
    else {
        $script:StaticAlgorithmVersion
    }
    Assert-AkburaTemplatePlanCondition (
        $Plan.AlgorithmVersion -eq $expectedAlgorithmVersion) (
        "Template verification algorithm '$($Plan.AlgorithmVersion)' " +
        "does not match '$expectedAlgorithmVersion'.")
    Assert-AkburaTemplatePlanCondition (
        [int] $Plan.Catalog.Count -eq $Catalog.Count) (
        "Template verification plan catalog count does not match.")
    Assert-AkburaTemplatePlanCondition (
        $Plan.Catalog.Sha256 -eq
            (Get-AkburaTemplateCatalogHash -Catalog $Catalog)) (
        "Template verification plan catalog hash does not match.")

    if ($null -ne $ExpectedReleaseContext) {
        foreach ($property in @(
            "SourceCommit",
            "SourceDirty",
            "AkburaVersion",
            "AvaloniaVersion")) {
            Assert-AkburaTemplatePlanCondition (
                $Plan.Source.$property -eq
                    $ExpectedReleaseContext.$property) (
                "Template verification plan source property '$property' " +
                "does not match the current verification input.")
        }
        $expectedPackages = @(
            $ExpectedReleaseContext.Packages |
                Sort-Object Name)
        $actualPackages = @($Plan.Source.Packages | Sort-Object Name)
        Assert-AkburaTemplatePlanCondition (
            $actualPackages.Count -eq $expectedPackages.Count) (
            "Template verification plan package count does not match.")
        for ($index = 0; $index -lt $expectedPackages.Count; $index++) {
            Assert-AkburaTemplatePlanCondition (
                $actualPackages[$index].Name -eq
                    $expectedPackages[$index].Name -and
                $actualPackages[$index].Sha256 -eq
                    $expectedPackages[$index].Sha256) (
                "Template verification plan package hash does not match " +
                "'$($expectedPackages[$index].Name)'.")
        }
    }

    $catalogLookup = Get-AkburaCaseLookup -Catalog $Catalog
    $caseLookup = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    foreach ($case in @($Plan.Cases)) {
        $caseId = Get-AkburaTemplateCaseId $case
        Assert-AkburaTemplatePlanCondition ($case.Id -eq $caseId) (
            "Template verification case ID '$($case.Id)' is not canonical.")
        Assert-AkburaTemplatePlanCondition (
            $catalogLookup.ContainsKey($case.Id)) (
            "Template verification case '$($case.Id)' is not in the catalog.")
        Assert-AkburaTemplatePlanCondition (
            !$caseLookup.ContainsKey($case.Id)) (
            "Template verification plan contains duplicate case " +
            "'$($case.Id)'.")
        $caseLookup.Add($case.Id, $case)
    }

    $structuralIds = @($Plan.StructuralCaseIds)
    Assert-AkburaTemplatePlanCondition (
        @($structuralIds | Sort-Object -Unique).Count -eq
            $structuralIds.Count) (
        "Template verification plan contains duplicate structural case IDs.")
    foreach ($caseId in $structuralIds) {
        Assert-AkburaTemplatePlanCondition ($caseLookup.ContainsKey($caseId)) (
            "Structural case '$caseId' is missing from plan cases.")
    }

    $executionKeys = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($execution in @($Plan.Executions)) {
        Assert-AkburaTemplatePlanCondition (
            $caseLookup.ContainsKey($execution.CaseId)) (
            "Execution case '$($execution.CaseId)' is missing from plan cases.")
        Assert-AkburaTemplatePlanCondition (
            $execution.Configuration -in @("Debug", "Release")) (
            "Execution '$($execution.CaseId)' has invalid configuration " +
            "'$($execution.Configuration)'.")
        $executionKey =
            "$($execution.CaseId)|$($execution.Configuration)"
        Assert-AkburaTemplatePlanCondition (
            $executionKeys.Add($executionKey)) (
            "Template verification plan contains duplicate execution " +
            "'$executionKey'.")
    }

    $executions = @($Plan.Executions)
    switch ($Plan.Mode) {
        "Sampled" {
            Assert-AkburaTemplatePlanCondition (
                $structuralIds.Count -eq 20 -and
                $executions.Count -eq 24) (
                "Sampled verification requires 20 cases and 24 executions.")
            $debugExecutions = @(
                $executions |
                    Where-Object Configuration -eq "Debug")
            $releaseExecutions = @(
                $executions |
                    Where-Object Configuration -eq "Release")
            Assert-AkburaTemplatePlanCondition (
                $debugExecutions.Count -eq 20 -and
                $releaseExecutions.Count -eq 4) (
                "Sampled verification requires 20 Debug and 4 Release executions.")
            $debugIds = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($execution in $debugExecutions) {
                [void] $debugIds.Add($execution.CaseId)
            }
            foreach ($execution in $releaseExecutions) {
                Assert-AkburaTemplatePlanCondition (
                    $debugIds.Contains($execution.CaseId)) (
                    "Release execution '$($execution.CaseId)' is not a " +
                    "selected Debug case.")
            }
            foreach ($kind in $script:Kinds) {
                foreach ($toolkit in $script:Toolkits) {
                    $matching = @($releaseExecutions | Where-Object {
                        $case = $caseLookup[$_.CaseId]
                        $case.Kind -eq $kind -and
                        $case.Toolkit -eq $toolkit
                    })
                    Assert-AkburaTemplatePlanCondition (
                        $matching.Count -eq 1) (
                        "Sampled Release executions do not cover " +
                        "$kind/$toolkit exactly once.")
                }
            }
            foreach ($toolkit in $script:Toolkits) {
                $xplatCases = @($Plan.Cases | Where-Object {
                    $_.Kind -eq "xplat" -and $_.Toolkit -eq $toolkit
                })
                Assert-AkburaTemplatePlanCondition (
                    $xplatCases.Count -eq 7) (
                    "Sampled verification requires seven xplat cases for " +
                    "$toolkit.")
                foreach ($pageType in $script:PageTypes) {
                    Assert-AkburaTemplatePlanCondition (
                        $xplatCases.PageType -contains $pageType) (
                        "Sampled xplat cases for $toolkit omit $pageType.")
                }
                foreach ($dependencyInjection in
                    $script:DependencyInjectionOptions) {
                    Assert-AkburaTemplatePlanCondition (
                        $xplatCases.DependencyInjection -contains
                            $dependencyInjection) (
                        "Sampled xplat cases for $toolkit omit " +
                        "$dependencyInjection.")
                }
            }
            $mvvmCases = @($Plan.Cases | Where-Object Kind -eq "mvvm")
            Assert-AkburaTemplatePlanCondition ($mvvmCases.Count -eq 6) (
                "Sampled verification requires six MVVM cases.")
            Assert-AkburaTemplatePlanCondition (
                $mvvmCases.Cpm -contains $true -and
                $mvvmCases.Cpm -contains $false -and
                $mvvmCases.RemoveViewLocator -contains $true -and
                $mvvmCases.RemoveViewLocator -contains $false) (
                "Sampled MVVM cases must include both boolean values.")
            foreach ($toolkit in $script:Toolkits) {
                foreach ($dependencyInjection in
                    $script:DependencyInjectionOptions) {
                    $matching = @($mvvmCases | Where-Object {
                        $_.Toolkit -eq $toolkit -and
                        $_.DependencyInjection -eq $dependencyInjection
                    })
                    Assert-AkburaTemplatePlanCondition (
                        $matching.Count -eq 1) (
                        "Sampled MVVM cases do not cover " +
                        "$toolkit/$dependencyInjection exactly once.")
                }
            }
        }
        "Smoke" {
            Assert-AkburaTemplatePlanCondition (
                $structuralIds.Count -eq 144 -and
                $executions.Count -eq 24) (
                "Smoke verification contract requires 144 structural cases " +
                "and 24 executions.")
            $expectedExecutionKeys = @(
                Get-AkburaSmokeExecutions -Catalog $Catalog |
                    ForEach-Object {
                        "$($_.CaseId)|$($_.Configuration)"
                    } |
                    Sort-Object)
            $actualExecutionKeys = @(
                $executions |
                    ForEach-Object {
                        "$($_.CaseId)|$($_.Configuration)"
                    } |
                    Sort-Object)
            Assert-AkburaTemplatePlanCondition (
                [string]::Join("`n", $actualExecutionKeys) -eq
                    [string]::Join("`n", $expectedExecutionKeys)) (
                "Smoke verification executions do not match the fixed " +
                "smoke contract.")
        }
        "Full" {
            Assert-AkburaTemplatePlanCondition (
                $structuralIds.Count -eq 144 -and
                $executions.Count -eq 288) (
                "Full verification requires 144 cases and 288 executions.")
        }
    }

    return $true
}

function Write-AkburaTemplateVerificationPlan {
    param(
        [Parameter(Mandatory)] $Plan,
        [Parameter(Mandatory)] [string] $Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    if (![string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $json = $Plan | ConvertTo-Json -Depth 12
    $json = ($json -replace "`r?`n", "`r`n") + "`r`n"
    [IO.File]::WriteAllText(
        $fullPath,
        $json,
        [Text.UTF8Encoding]::new($false))
    return $fullPath
}

function Read-AkburaTemplateVerificationPlan {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object[]] $Catalog,
        $ExpectedReleaseContext
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-AkburaTemplatePlanCondition (
        Test-Path -LiteralPath $fullPath -PathType Leaf) (
        "Template verification plan does not exist: $fullPath")
    try {
        $plan = Get-Content -LiteralPath $fullPath -Raw |
            ConvertFrom-Json -Depth 12
    }
    catch {
        throw "Template verification plan '$fullPath' is invalid JSON: $_"
    }
    [void] (Test-AkburaTemplateVerificationPlan `
        -Plan $plan `
        -Catalog $Catalog `
        -ExpectedReleaseContext $ExpectedReleaseContext)
    return $plan
}

function Resolve-AkburaTemplateVerificationPlan {
    param(
        [Parameter(Mandatory)] [ValidateSet("Sampled", "Smoke", "Full")]
        [string] $Mode,
        [Parameter(Mandatory)] [object[]] $Catalog,
        [Parameter(Mandatory)] $ReleaseContext,
        [string] $SelectionManifest,
        [string] $PlanOutputPath,
        [scriptblock] $RandomIndexProvider
    )

    if (![string]::IsNullOrWhiteSpace($SelectionManifest)) {
        $plan = Read-AkburaTemplateVerificationPlan `
            -Path $SelectionManifest `
            -Catalog $Catalog `
            -ExpectedReleaseContext $ReleaseContext
        Assert-AkburaTemplatePlanCondition ($plan.Mode -eq $Mode) (
            "Replay plan mode '$($plan.Mode)' does not match requested " +
            "mode '$Mode'.")
        return $plan
    }

    $plan = New-AkburaTemplateVerificationPlan `
        -Mode $Mode `
        -Catalog $Catalog `
        -ReleaseContext $ReleaseContext `
        -RandomIndexProvider $RandomIndexProvider
    [void] (Test-AkburaTemplateVerificationPlan `
        -Plan $plan `
        -Catalog $Catalog `
        -ExpectedReleaseContext $ReleaseContext)
    if (![string]::IsNullOrWhiteSpace($PlanOutputPath)) {
        [void] (Write-AkburaTemplateVerificationPlan `
            -Plan $plan `
            -Path $PlanOutputPath)
    }
    return $plan
}

Export-ModuleMember -Function @(
    "Get-AkburaTemplateCaseCatalog",
    "Get-AkburaTemplateCatalogHash",
    "Get-AkburaTemplateCaseId",
    "Get-AkburaSmokeExecutions",
    "New-AkburaTemplateVerificationPlan",
    "Read-AkburaTemplateVerificationPlan",
    "Resolve-AkburaTemplateVerificationPlan",
    "Test-AkburaTemplateVerificationPlan",
    "Write-AkburaTemplateVerificationPlan")
