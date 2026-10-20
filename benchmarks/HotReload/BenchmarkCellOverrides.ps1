function Get-BenchmarkCellOverrides([string[]]$Component, [string[]]$Scenario, [string[]]$Override) {
    $map = @{}
    foreach ($item in $Override) {
        if ($item -notmatch '^(?<Workload>gallery|purity):(?<Source>[^\\/:=]+\.akbura)/(?<Scenario>[^\\/:=]+)=(?<Target>[^\\/:=]+\.akbura)$') {
            throw "Invalid benchmark cell override: $item"
        }
        $componentName = $Matches.Workload + ':' + $Matches.Source
        $key = $componentName + '/' + $Matches.Scenario
        if ($componentName -notin $Component -or $Matches.Scenario -notin $Scenario -or $map.ContainsKey($key)) {
            throw "Unknown or duplicate benchmark cell override: $item"
        }
        $map[$key] = $Matches.Target
    }
    $actualCells = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Component) {
        $workload,$file = $item.Split(':')
        foreach ($edit in $Scenario) {
            $key = $item + '/' + $edit
            $actualFile = if ($map.ContainsKey($key)) { $map[$key] } else { $file }
            if (-not $actualCells.Add($workload + ':' + $actualFile + '/' + $edit)) {
                throw "Component routing creates a duplicate benchmark cell: $key"
            }
        }
    }
    return $map
}
