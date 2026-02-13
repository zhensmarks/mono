$content = Get-Content "Views\LogPanelSidebar.axaml.cs"
$balance = 0
foreach ($line in $content) {
    # Ignore comments
    $cleanLine = $line -replace "//.*", ""
    $chars = $cleanLine.ToCharArray()
    foreach ($ch in $chars) {
        if ($ch -eq '{') { $balance++ }
        if ($ch -eq '}') { $balance-- }
    }
}
Write-Host "For Views\LogPanelSidebar.axaml.cs Final Balance: $balance"
