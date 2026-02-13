using System;
using System.Threading.Tasks;
using BMachine.SDK;

namespace BMachine.UI.Services;

public class NotificationService : INotificationService
{
    public void ShowInfo(string message, string? title = null)
    {
        TriggerWindowsNotification(title ?? "Info", message, "Info");
    }

    public void ShowSuccess(string message, string? title = null)
    {
        TriggerWindowsNotification(title ?? "Success", message, "Info");
    }

    public void ShowWarning(string message, string? title = null)
    {
         TriggerWindowsNotification(title ?? "Warning", message, "Warning");
    }

    public void ShowError(string message, string? title = null)
    {
         TriggerWindowsNotification(title ?? "Error", message, "Error");
    }

    public Task<bool> ShowConfirmAsync(string message, string? title = null)
    {
        // For now, return true or throw not implemented for specific UI confirm
        // To implement properly, we need a Dialog Service or MessageBox
        System.Diagnostics.Debug.WriteLine($"[Confirm] {title}: {message}");
        return Task.FromResult(true); 
    }

    private void TriggerWindowsNotification(string title, string message, string icon)
    {
         // Check if "Notifikasi" extension is enabled (file exists in Plugins)
         // Or just use PowerShell directly since this is a UI service
         
         Task.Run(() => 
         {
             try 
             {
                 // Icon: Info, Warning, Error, None
                 var ps = $"& {{Add-Type -AssemblyName System.Windows.Forms; $n = New-Object System.Windows.Forms.NotifyIcon; $n.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Get-Process -Id $pid).Path); $n.Visible = $True; $n.ShowBalloonTip(3000, '{title}', '{message}', [System.Windows.Forms.ToolTipIcon]::{icon}); Start-Sleep 3; $n.Dispose()}}";
                 var info = new System.Diagnostics.ProcessStartInfo
                 {
                     FileName = "powershell",
                     Arguments = $"-WindowStyle Hidden -Command \"{ps.Replace("\"", "\\\"")}\"",
                     UseShellExecute = false,
                     CreateNoWindow = true
                 };
                 System.Diagnostics.Process.Start(info);
             }
             catch (Exception ex) 
             { 
                 System.Diagnostics.Debug.WriteLine($"Notif Failed: {ex.Message}"); 
             }
         });
    }
}
