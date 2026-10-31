using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using BMachine.UI.Views.Dialogs;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Ukuran nyata dialog update setelah di-layout, bukan sekadar atribut XAML.
/// Keluhan nyata dari user (diukur pakai Power Ruler): tombol "Nanti" 32px
/// sementara "Perbarui sekarang" 34px, sehingga baris aksi tidak sejajar.
/// </summary>
public class UpdateDialogLayoutTests
{
    private static Button FindButton(Visual root, string label) =>
        root.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));

    [AvaloniaFact]
    public void TombolAksiTerrenderDenganTinggiSama()
    {
        var window = new UpdateDialogWindow();
        try
        {
            window.Show();

            var apply = FindButton(window, "Perbarui sekarang");
            var dismiss = FindButton(window, "Nanti");

            Assert.Equal(apply.Bounds.Height, dismiss.Bounds.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>Ketebalan border harus sama. Dulu "Nanti" BorderThickness=1 sementara
    /// "Perbarui sekarang" 0, sehingga isi yang terlihat tingginya 32px vs 34px
    /// (34 dikurangi 1px border atas+bawah) — persis yang diukur user dengan penggaris.</summary>
    [AvaloniaFact]
    public void KetebalanBorderTombolAksiSama()
    {
        var window = new UpdateDialogWindow();
        try
        {
            window.Show();

            var apply = FindButton(window, "Perbarui sekarang");
            var dismiss = FindButton(window, "Nanti");

            Assert.Equal(apply.BorderThickness, dismiss.BorderThickness);
        }
        finally { window.Close(); }
    }

    /// <summary>Area isi (ContentPresenter) juga harus sama tinggi.</summary>
    [AvaloniaFact]
    public void IsiTombolAksiTerrenderDenganTinggiSama()
    {
        var window = new UpdateDialogWindow();
        try
        {
            window.Show();

            var apply = FindButton(window, "Perbarui sekarang");
            var dismiss = FindButton(window, "Nanti");

            // Cari berdasarkan nama tipe: namespace ContentPresenter berbeda antar versi Avalonia.
            var applyPresenter = apply.GetVisualDescendants().First(d => d.GetType().Name == "ContentPresenter");
            var dismissPresenter = dismiss.GetVisualDescendants().First(d => d.GetType().Name == "ContentPresenter");

            Assert.Equal(applyPresenter.Bounds.Height, dismissPresenter.Bounds.Height);
        }
        finally { window.Close(); }
    }

    }