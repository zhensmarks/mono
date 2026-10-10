using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace BMachine.UI.Controls;

/// <summary>
/// Mencegah lompatan scroll saat klik biasa di dalam panel scrollable, sambil tetap
/// mengizinkan auto-scroll saat drag-select melewati batas viewport.
///
/// Cara kerja: catat Offset saat PointerPressed. Saat PointerReleased, kalau pointer
/// TIDAK bergerak (klik tunggal, bukan drag) tapi Offset berubah, kembalikan Offset
/// awal. Drag (pointer bergerak &gt; threshold) dibiarkan, sehingga auto-scroll
/// seleksi teks tetap bekerja normal.
/// </summary>
public static class PointerScrollGuard
{
    private const double ClickThreshold = 4; // DIP; di bawah ini dianggap klik, bukan drag

    private sealed class State
    {
        public Vector StartOffset;
        public Point StartPos;
        public bool Pressed;
    }

    /// <summary>Pasang guard pada satu ScrollViewer; owner = kontrol yang menerima pointer (mis. panel atau host).</summary>
    public static void Attach(Control owner, ScrollViewer viewer)
    {
        var state = new State();

        owner.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed) return;
            state.StartOffset = viewer.Offset;
            state.StartPos = e.GetPosition(owner);
            state.Pressed = true;
        };

        owner.PointerReleased += (_, e) =>
        {
            if (!state.Pressed) return;
            state.Pressed = false;

            var pos = e.GetPosition(owner);
            bool isClick = Math.Abs(pos.X - state.StartPos.X) < ClickThreshold
                        && Math.Abs(pos.Y - state.StartPos.Y) < ClickThreshold;
            if (isClick && viewer.Offset != state.StartOffset)
                viewer.Offset = state.StartOffset;
        };
    }
}