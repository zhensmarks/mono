using System.Reflection;
using System.Threading.Tasks;
using Abeng.BugReporter;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace BMachine.UI.Views.Dialogs.MantraData;

/// <summary>
/// Dialog "Lapork Bug" manual. Mengirim ke ProjectBot (HTTP lokal) lewat
/// BugReporter. Kegagalan jaringan tidak menutup dialog dengan error keras -
/// user diberi tahu laporan tersimpan lokal &amp; bisa dicoba lagi.
///
/// UI mengikuti token MantraData (AGENTS.md): Button.Action / Button.Muted /
/// Button.WindowClose, tanpa ikon &amp; tanpa glyph.
/// </summary>
public partial class ReportBugDialog : MantraDialogBase
{
    private readonly BugReporter _reporter;

    // Project name terdaftar di config ProjectBot. Konstruktor tanpa parameter
    // wajib agar AXAML bisa instantiate (compiled bindings).
    public ReportBugDialog()
    {
        InitializeComponent();
        _reporter = new BugReporter("BMachine.v2");
    }

    /// <summary>True kalau laporan berhasil dikirim (untuk pemanggil). Werjilnull=belum.</summary>
    public bool? SendResult { get; private set; }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        var summary = (SummaryBox.Text ?? string.Empty).Trim();
        var detail = (DetailBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(summary))
        {
            SummaryBox.Focus();
            return;
        }

        // Nonaktifkan tombol saat mengirim supaya tidak double-submit.
        if (sender is Button btn) btn.IsEnabled = false;

        var ok = await _reporter.SendAsync(
            summary: summary,
            text: string.IsNullOrEmpty(detail) ? summary : detail,
            appVersion: AppVersion(),
            severity: "sedang",
            source: "manual");

        SendResult = ok;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ok)
            {
                // Jangan pakai dialog pop-up baru (noise). Tutup saja; pemanggil
                // bisa baca SendResult untuk menampilkan toast di tempat yang tepat.
                Close();
            }
            else
            {
                // ProjectBot mati / port tutup -> kirim ulang tetap mungkin.
                if (sender is Button b) b.IsEnabled = true;
            }
        });
    }

    // Baca <Version> dari assembly app (AssemblyInformationalVersion).
    private static string? AppVersion()
    {
        try
        {
            var v = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v.Split('+')[0];
        }
        catch { return null; }
    }
}
