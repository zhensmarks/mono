$content = Get-Content "Views\DashboardView.axaml.cs"
$balance = 0
$lineno = 0
foreach ($line in $content) {
    $lineno++
    # Ignore comments (simple check regex)
    $cleanLine = $line -replace "//.*", ""
    $chars = $cleanLine.ToCharArray()
    foreach ($ch in $chars) {
        if ($ch -eq '{') { $balance++ }
        if ($ch -eq '}') { $balance-- }
    }
    if ($balance -le 0 -and $lineno -gt 25) {
        Write-Host "Balance hit $balance at line $lineno"
        break
    }
}
