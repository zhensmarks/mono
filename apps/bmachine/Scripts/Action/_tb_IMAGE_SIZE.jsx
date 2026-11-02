#target photoshop

/*
    _tb_IMAGE_SIZE.jsx
    Fitur: Ubah ukuran dokumen aktif / buat dokumen baru dari preset ukuran cetak.

    Preset:
      8R   25 x 20 cm
      10RP 38 x 25 cm
      12RP 45 x 30 cm
      16RP 60 x 40 cm
      24RP 34 x 24 in
      A3   44.5 x 29.5 cm
*/

(function () {
    // === UTILS: Window Position Persistence ===
    function loadWindowLocation() {
        var f = new File(Folder.userData + "/bmachine_image_size_pos.json");
        if (f.exists) {
            f.open("r");
            var data = f.read();
            f.close();
            try {
                var obj = eval("(" + data + ")");
                if (obj && typeof obj.x === 'number' && typeof obj.y === 'number') return obj;
            } catch (e) { }
        }
        return null;
    }

    function saveWindowLocation(loc) {
        if (!loc) return;
        var f = new File(Folder.userData + "/bmachine_image_size_pos.json");
        f.open("w");
        f.write('{"x": ' + Math.round(loc.x) + ', "y": ' + Math.round(loc.y) + '}');
        f.close();
    }

    // === PRESETS ===
    var PRESETS = [
        { label: "8R   (25 x 20 cm)",   w: 25,   h: 20,   unit: "cm" },
        { label: "10RP (38 x 25 cm)",   w: 38,   h: 25,   unit: "cm" },
        { label: "12RP (45 x 30 cm)",   w: 45,   h: 30,   unit: "cm" },
        { label: "16RP (60 x 40 cm)",   w: 60,   h: 40,   unit: "cm" },
        { label: "24RP (34 x 24 in)",   w: 34,   h: 24,   unit: "in" },
        { label: "A3   (44.5 x 29.5 cm)", w: 44.5, h: 29.5, unit: "cm" }
    ];

    // === UI ===
    var w = new Window("dialog", "IMAGE SIZE");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 10;
    w.margins = 16;

    // --- SECTION: PRESET ---
    var pnlPreset = w.add("panel", undefined, "Pilih Ukuran");
    pnlPreset.orientation = "column";
    pnlPreset.alignChildren = ["fill", "top"];
    pnlPreset.spacing = 6;

    var presetLabels = [];
    for (var i = 0; i < PRESETS.length; i++) presetLabels.push(PRESETS[i].label);
    var drpPreset = pnlPreset.add("dropdownlist", undefined, presetLabels);
    drpPreset.selection = 0;

    // --- SECTION: ORIENTASI ---
    var pnlOrientasi = w.add("panel", undefined, "Orientasi");
    pnlOrientasi.orientation = "row";
    pnlOrientasi.alignChildren = ["left", "center"];
    pnlOrientasi.spacing = 12;

    var rbLandscape = pnlOrientasi.add("radiobutton", undefined, "Landscape");
    var rbPortrait = pnlOrientasi.add("radiobutton", undefined, "Portrait");
    rbLandscape.value = true;

    // --- SECTION: AKSI ---
    var grpBtn = w.add("group");
    grpBtn.alignment = "center";
    grpBtn.spacing = 10;
    var btnResize = grpBtn.add("button", undefined, "RESIZE DOKUMEN AKTIF");
    var btnNewDoc = grpBtn.add("button", undefined, "DOKUMEN BARU");
    var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });

    // === LOGIC ===
    function getSize() {
        var idx = drpPreset.selection.index;
        var p = PRESETS[idx];
        var width = p.w, height = p.h;
        if (rbPortrait.value) {
            var t = width; width = height; height = t;
        }
        return { width: width, height: height, unit: p.unit };
    }

    btnResize.onClick = function () {
        if (app.documents.length === 0) { alert("Tidak ada dokumen yang terbuka!"); return; }
        var s = getSize();
        var doc = app.activeDocument;
        try {
            doc.resizeImage(
                UnitValue(s.width, s.unit),
                UnitValue(s.height, s.unit),
                300,
                ResampleMethod.BICUBIC
            );
        } catch (e) {
            alert("Gagal resize: " + e);
            return;
        }
        w.close(1);
    };

    btnNewDoc.onClick = function () {
        var s = getSize();
        try {
            app.documents.add(
                UnitValue(s.width, s.unit),
                UnitValue(s.height, s.unit),
                300,
                "Dokumen Baru",
                NewDocumentMode.RGB,
                DocumentFill.WHITE
            );
        } catch (e) {
            alert("Gagal membuat dokumen: " + e);
            return;
        }
        w.close(1);
    };

    btnCancel.onClick = function () { w.close(0); };

    // Restore Position
    var savedLoc = loadWindowLocation();
    if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
    else w.center();

    var res = w.show();
    if (res != 0) saveWindowLocation(w.location);
})();
