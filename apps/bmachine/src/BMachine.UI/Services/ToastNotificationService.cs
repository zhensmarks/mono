using System;
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

    public Task<bool> ShowConfirmAsync(string message, string? title = null)
    {
        // Confirmation dialog belum diimplementasi (butuh modal dialog, bukan toast)
        // Untuk sementara, log ke console dan return true
        System.Diagnostics.Debug.WriteLine($"[Confirm] {title}: {message}");
        return Task.FromResult(true);
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
