using System;
using System.Threading.Tasks;

namespace TrelloCompact.Services;

public class NotificationService
{
    public void ShowInfo(string message, string title = "TrelloCompact")
    {
        TriggerWindowsNotification(title, message, "Info");
    }

    public void ShowSuccess(string message, string title = "TrelloCompact")
    {
        TriggerWindowsNotification(title, message, "Info");
    }

    public void ShowWarning(string message, string title = "TrelloCompact")
    {
         TriggerWindowsNotification(title, message, "Warning");
    }

    public void ShowError(string message, string title = "TrelloCompact")
    {
         TriggerWindowsNotification(title, message, "Error");
    }

    private void TriggerWindowsNotification(string title, string message, string icon)
    {
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
