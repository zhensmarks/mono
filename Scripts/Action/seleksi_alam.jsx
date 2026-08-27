#target photoshop

/*
    seleksi_alam.jsx
    Fitur: Menu Seleksi Layer Otomatis (Hide/Show specific layers)
*/

(function () {
    if (app.documents.length === 0) {
        alert("Tidak ada dokumen yang terbuka!");
        return;
    }

    // === UTILS: Window Position Persistence ===
    function loadWindowLocation() {
        var f = new File(Folder.userData + "/bmachine_seleksi_alam_pos.json");
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
        var f = new File(Folder.userData + "/bmachine_seleksi_alam_pos.json");
        f.open("w");
        f.write('{"x": ' + Math.round(loc.x) + ', "y": ' + Math.round(loc.y) + '}');
        f.close();
    }

    // === CORE LOGIC ===
    function findLayerRecursive(root, name) {
        // 1. Cek di level saat ini
        try { return root.layers.getByName(name); } catch (e) { }

        // 2. Cek di dalam Group (LayerSet)
        for (var i = 0; i < root.layers.length; i++) {
            if (root.layers[i].typename == "LayerSet") {
                var found = findLayerRecursive(root.layers[i], name);
                if (found) return found;
            }
        }
        return null; // Akan otomatis skip (return null) jika tidak ditemukan
    }

    function hideLayers(doc, names) {
        for (var i = 0; i < names.length; i++) {
            var layer = findLayerRecursive(doc, names[i]);
            if (layer) layer.visible = false;
        }
    }

    function processDocument(mode, isBatch) {
        if (isBatch) {
            for (var i = 0; i < app.documents.length; i++) {
                app.activeDocument = app.documents[i];
                applyToDoc(app.activeDocument, mode);
            }
        } else {
            applyToDoc(app.activeDocument, mode);
        }
    }

    function applyToDoc(doc, mode) {
        var baseName = doc.name.replace(/\.[^\.]+$/, ""); // Ambil nama file tanpa ekstensi (misal "3" dari "3.psd")

        if (mode === 1) {
            // 1. ANAK SAJA
            var list = ["REDAKSI SEKOLAH", "ASET", "KOLASE", "BG KOLASE", "BG", "PROP", "KARPET", "karpet"];
            hideLayers(doc, list);
        }
        else if (mode === 2) {
            // 2. ORTU SAJA
            var list = ["REDAKSI SEKOLAH", "ASET", "BG", "PROP", "KARPET", "karpet", "BG KOLASE", "UTAMA"];
            
            // 1. Selalu hide X(1) dan X (1)
            list.push(baseName + "(1)", baseName + " (1)");
            
            // 2. Cek apakah ada layer X(3) atau X (3)
            var hasLayer3 = findLayerRecursive(doc, baseName + "(3)") || findLayerRecursive(doc, baseName + " (3)");
            
            // 3. Hide X(2) dan X (2) HANYA JIKA X(3) ada
            if (hasLayer3) {
                list.push(baseName + "(2)", baseName + " (2)");
            }
            
            hideLayers(doc, list);
        }
    }

    // === UI ===
    var w = new Window("dialog", "Seleksi Alam");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 10;
    w.margins = 16;

    var grp = w.add("group");
    grp.orientation = "row";
    grp.alignChildren = ["fill", "top"];
    grp.spacing = 10;

    // Kolom 1 (Kiri)
    var btnAnak = grp.add("button", [0, 0, 140, 50], "ANAK SAJA");

    // Kolom 2 (Kanan)
    var btnOrtu = grp.add("button", [0, 0, 140, 50], "ORTU SAJA");

    // Batch Checkbox
    var cbBatch = w.add("checkbox", undefined, "Batch (Semua File Terbuka)");
    cbBatch.alignment = "center";

    var btnCancel = w.add("button", undefined, "Cancel");

    // Handlers
    btnAnak.onClick = function () { processDocument(1, cbBatch.value); w.close(); };
    btnOrtu.onClick = function () { processDocument(2, cbBatch.value); w.close(); };
    btnCancel.onClick = function () { w.close(); };

    // Restore Position
    var savedLoc = loadWindowLocation();
    if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
    else w.center();

    // Show
    var res = w.show();
    if (res != 2) {
        saveWindowLocation(w.location);
    }
})();
