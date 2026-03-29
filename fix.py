import re

file_path = 'src/BMachine.UI/ViewModels/SettingsViewModel.cs'

with open(file_path, 'r') as f:
    lines = f.readlines()

def remove_lines(start_str, end_str):
    start_idx = -1
    end_idx = -1
    for i, line in enumerate(lines):
        if start_str in line and start_idx == -1:
            start_idx = i
        if start_idx != -1 and end_str in line:
            end_idx = i
            break
    if start_idx != -1 and end_idx != -1:
        for i in range(start_idx, end_idx + 1):
            lines[i] = ''

# Remove from // --- Sync Additional Colors --- to end of method
remove_lines('// --- Sync Additional Colors ---', '_themeService.SetTerminalBackgroundColor(CustomLightTerminalBgColor.ToString(), false, saveToDb: false);')

# Remove from // Load Background Colors to // Load Floating Widget
for i, line in enumerate(lines):
    if '// Load Background Colors' in line:
        start_idx = i
        # find floating widget
        for j in range(i, len(lines)):
            if '// Load Floating Widget' in lines[j]:
                end_idx = j
                for k in range(start_idx, end_idx):
                    lines[k] = ''
                break
        break

with open(file_path, 'w') as f:
    f.writelines(lines)
print("Fixed SettingsViewModel")
