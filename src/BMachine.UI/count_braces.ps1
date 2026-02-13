$files = @("ViewModels\BatchViewModel.cs", "Views\DashboardView.axaml.cs", "Views\LogPanelSidebar.axaml.cs"); 
foreach ($f in $files) { 
    if (Test-Path $f) {
        $txt = Get-Content $f -Raw; 
        $open = ($txt.ToCharArray() | Where-Object { $_ -eq '{' }).Count; 
        $close = ($txt.ToCharArray() | Where-Object { $_ -eq '}' }).Count; 
        Write-Output "$f : Open $open Close $close Diff $($open - $close)" 
    } else {
        Write-Output "$f : File not found"
    }
}
