param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$trxFiles = @(Get-ChildItem -Path $Path -Filter '*.trx' -File -ErrorAction SilentlyContinue)

if ($trxFiles.Count -eq 0) {
    throw "No TRX result files were produced under '$Path'. Test execution cannot be considered successful."
}

foreach ($trxFile in $trxFiles) {
    [xml]$document = Get-Content -Path $trxFile.FullName -Raw
    $counters = $document.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")

    if ($null -eq $counters) {
        throw "TRX '$($trxFile.Name)' does not contain ResultSummary/Counters."
    }

    $total = [int]$counters.GetAttribute('total')
    $executed = [int]$counters.GetAttribute('executed')

    if ($total -le 0 -or $executed -le 0) {
        throw "TRX '$($trxFile.Name)' reported total=$total, executed=$executed. A green build must execute at least one discovered test."
    }

    Write-Host "Verified test execution: $($trxFile.Name) total=$total executed=$executed"
}
