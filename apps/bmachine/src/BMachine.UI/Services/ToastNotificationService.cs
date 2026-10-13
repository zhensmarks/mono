using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using BMachine.SDK;
using BMachine.UI.Views.Controls;

namespace BMachine.UI.Services;

/// <summary>
/// In-app toast notification service dengan queue support.
/// Singleton pattern untuk akses global dari ViewModel.
/// </summary>
public class ToastNotificationService : INotificationService
{
    private static ToastNotificationService? _instance;
    private InAppToast? _toastControl;
    private readonly Queue<ToastMessage> _messageQueue = new();
    private bool _isShowingToast;

    public static ToastNotificationService Instance => _instance ??= new ToastNotificationService();

    private ToastNotificationService() { }

    /// <summary>
    /// Register InAppToast control dari DashboardView.
    /// Dipanggil saat view loaded.
    /// </summary>
    public void RegisterToastControl(InAppToast toast)
    {
        _toastControl = toast;
    }

    public void ShowInfo(string message, string? title = null)
    {
        EnqueueToast(message, ToastType.Info);
    }

    public void ShowSuccess(string message, string? title = null)
    {
        EnqueueToast(message, ToastType.Success);
    }

    public void ShowWarning(string message, string? title = null)
    {
        EnqueueToast(message, ToastType.Warning);
    }

    public void ShowError(string message, string? title = null)
    {
        EnqueueToast(message, ToastType.Error);
    }

    /// <summary>
    /// Real modal confirmation (was a stub that always returned true, which made confirmations
    /// meaningless — dangerous for irreversible actions such as a permanent delete).
    /// Fail-safe: if no window can host the dialog we return FALSE, so the caller does nothing.
    /// </summary>
    public async Task<bool> ShowConfirmAsync(string message, string? title = null)
    {
        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                return await ShowConfirmCoreAsync(message, title);

            var task = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => ShowConfirmCoreAsync(message, title));
            return await task;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Confirm] failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> ShowConfirmCoreAsync(string message, string? title)
    {
        var dialog = new Views.Dialogs.MantraData.MantraConfirmDialog(title ?? "Konfirmasi", message);

        var owner = ResolveOwnerWindow();
        if (owner == null)
        {
            System.Diagnostics.Debug.WriteLine("[Confirm] no owner window - refusing to guess an answer");
            return false;
        }

        return await dialog.ShowDialog<bool>(owner);
    }

    private static Avalonia.Controls.Window? ResolveOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible)
                ?? desktop.Windows.FirstOrDefault(w => w.IsVisible)
                ?? desktop.MainWindow;
        }

        return null;
    }

    private void EnqueueToast(string message, ToastType type)
    {
        if (_toastControl == null)
        {
            // Fallback: jika toast belum registered, log ke console
            System.Diagnostics.Debug.WriteLine($"[Toast] {type}: {message}");
            return;
        }

        _messageQueue.Enqueue(new ToastMessage { Message = message, Type = type });

        if (!_isShowingToast)
        {
            _ = ProcessQueueAsync();
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (_toastControl == null) return;

        _isShowingToast = true;

        while (_messageQueue.Count > 0)
        {
            var toast = _messageQueue.Dequeue();
            
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await _toastControl.ShowAsync(toast.Message, toast.Type, durationMs: 3000);
                });

                // Delay sebelum toast berikutnya (agar tidak langsung tumpuk)
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToastNotificationService] Error: {ex.Message}");
            }
        }

        _isShowingToast = false;
    }

    private class ToastMessage
    {
        public string Message { get; set; } = string.Empty;
        public ToastType Type { get; set; }
    }
}
