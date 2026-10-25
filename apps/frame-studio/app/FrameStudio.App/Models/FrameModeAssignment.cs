using FrameStudio.Engine;

namespace FrameStudio.App.Models;

public enum FrameAssignmentMode
{
    Single,
    Kolase
}

public sealed record FrameAssignmentTargetStatus(
    FrameLayerRole Role,
    bool Found,
    bool Ready,
    bool Ambiguous,
    string Message);

public sealed record FrameModeAssignmentPlan(
    FrameAssignmentMode Mode,
    IReadOnlyList<FrameLayerRole> PhotoRoles,
    FrameAssignmentTargetStatus XlTarget,
    FrameAssignmentTargetStatus STarget,
    FrameAssignmentTargetStatus SmallBackground,
    bool TargetsReady,
    bool CanExportPng,
    string StatusText)
{
    public static FrameModeAssignmentPlan Create(
        FrameAssignmentMode mode,
        IEnumerable<FrameSlot> slots,
        FramePsdLayerTreeSummary? layerTree)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var slotList = slots.ToArray();
        var layerList = layerTree is null
            ? Array.Empty<FramePsdLayerSummary>()
            : Flatten(layerTree.Roots).ToArray();

        var xl = ResolvePhotoTarget(FrameLayerRole.XLPhoto, "XL", slotList, layerList, layerTree is not null);
        var small = ResolvePhotoTarget(FrameLayerRole.SPhoto, "S", slotList, layerList, layerTree is not null);
        var background = ResolveGroupTarget(layerList);
        var photoRoles = mode == FrameAssignmentMode.Single
            ? new[] { FrameLayerRole.XLPhoto }
            : new[] { FrameLayerRole.XLPhoto, FrameLayerRole.SPhoto };

        var targetsReady = xl.Ready
            && (mode == FrameAssignmentMode.Single || small.Ready && background.Ready);
        var canExportPng = mode == FrameAssignmentMode.Single && targetsReady;
        var status = BuildStatus(mode, xl, small, background, targetsReady);

        return new FrameModeAssignmentPlan(
            mode,
            Array.AsReadOnly(photoRoles),
            xl,
            small,
            background,
            targetsReady,
            canExportPng,
            status);
    }

    public bool IncludesPhotoRole(FrameLayerRole role) => PhotoRoles.Contains(role);

    private static FrameAssignmentTargetStatus ResolvePhotoTarget(
        FrameLayerRole role,
        string label,
        IReadOnlyList<FrameSlot> slots,
        IReadOnlyList<FramePsdLayerSummary> layers,
        bool hasLayerTree)
    {
        var roleLayers = layers.Where(layer => layer.AssignmentRole == role).ToArray();
        var roleSlots = slots.Where(slot => slot.AssignmentRole == role).ToArray();
        var ambiguous = roleLayers.Length > 1 || roleSlots.Length > 1;
        if (ambiguous)
            return new(role, true, false, true, $"{label}: ada beberapa kandidat; assignment ambigu.");

        var found = roleLayers.Length == 1 || roleSlots.Length == 1;
        if (!found)
            return new(role, false, false, false, $"{label}: target tidak ditemukan.");

        var layerReady = !hasLayerTree || roleLayers.Length == 1 && roleLayers[0].IsPlacementReady && !roleLayers[0].IsGroup;
        var slotReady = roleSlots.Length == 1 && roleSlots[0].IsPlacementReady;
        var ready = layerReady && slotReady;
        return new(
            role,
            true,
            ready,
            false,
            ready
                ? $"{label}: target foto valid."
                : !layerReady
                    ? $"{label}: target ada, tetapi batas positif dan mask/clipping tervalidasi tidak lengkap."
                    : $"{label}: metadata valid, tetapi slot assignment usable tidak diekspos; diblokir.");
    }

    private static FrameAssignmentTargetStatus ResolveGroupTarget(IReadOnlyList<FramePsdLayerSummary> layers)
    {
        var groups = layers.Where(layer => layer.AssignmentRole == FrameLayerRole.BGKecil).ToArray();
        if (groups.Length > 1)
            return new(FrameLayerRole.BGKecil, true, false, true, "BG kecil: ada beberapa grup kandidat; assignment ambigu.");
        if (groups.Length == 0)
            return new(FrameLayerRole.BGKecil, false, false, false, "BG kecil: grup tidak ditemukan.");
        if (!groups[0].IsGroup || groups[0].Children.Count == 0)
            return new(FrameLayerRole.BGKecil, true, false, false, "BG kecil: grup tidak memiliki isi layer yang dapat diaktifkan.");
        return new(FrameLayerRole.BGKecil, true, true, false, "BG kecil: grup ditemukan; visibilitasnya belum dapat diubah.");
    }

    private static string BuildStatus(
        FrameAssignmentMode mode,
        FrameAssignmentTargetStatus xl,
        FrameAssignmentTargetStatus small,
        FrameAssignmentTargetStatus background,
        bool ready)
    {
        var required = mode == FrameAssignmentMode.Single
            ? new[] { xl }
            : new[] { xl, small, background };
        if (!ready)
            return string.Join(" ", required.Select(target => target.Message));

        return mode == FrameAssignmentMode.Single
            ? "Single siap untuk pratinjau/PNG raster perkiraan pada XL. PSD berlapis tetap tidak disimpan."
            : "Target Kolase terpetakan (XL + S + BG kecil), tetapi grup BG kecil belum dapat diaktifkan dan ekspor hasil Kolase dinonaktifkan.";
    }

    private static IEnumerable<FramePsdLayerSummary> Flatten(IEnumerable<FramePsdLayerSummary> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }
}
