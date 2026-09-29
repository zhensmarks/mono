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

    function processDocument(list, useOrtuLogic, isBatch) {
        if (isBatch) {
            for (var i = 0; i < app.documents.length; i++) {
                app.activeDocument = app.documents[i];
                applyToDoc(app.activeDocument, list, useOrtuLogic);
            }
        } else {
            applyToDoc(app.activeDocument, list, useOrtuLogic);
        }
    }

    function applyToDoc(doc, list, useOrtuLogic) {
        var baseName = doc.name.replace(/\.[^\.]+$/, ""); // Ambil nama file tanpa ekstensi (misal "3" dari "3.psd")

        var finalList = list.slice(); // Copy

        if (useOrtuLogic) {
            // 1. Selalu hide X(1) dan X (1)
            finalList.push(baseName + "(1)", baseName + " (1)");
            
            // 2. Cek apakah ada layer X(3) atau X (3)
            var hasLayer3 = findLayerRecursive(doc, baseName + "(3)") || findLayerRecursive(doc, baseName + " (3)");
            
            // 3. Hide X(2) dan X (2) HANYA JIKA X(3) ada
            if (hasLayer3) {
                finalList.push(baseName + "(2)", baseName + " (2)");
            }

            // 4. Jika "BG KOLASE" tidak di-hide, lepaskan clipping mask dari "BG copy"
            var bgKolaseHidden = false;
            for (var k = 0; k < list.length; k++) {
                if (list[k] === "BG KOLASE") {
                    bgKolaseHidden = true;
                    break;
                }
            }
            if (!bgKolaseHidden) {
                var bgCopyLayer = findLayerRecursive(doc, "BG copy");
                if (bgCopyLayer && bgCopyLayer.grouped) {
                    try {
                        bgCopyLayer.grouped = false;
                    } catch (e) {
                        // Abaikan jika tidak bisa dilepas
                    }
                }
            }
        }
        
        hideLayers(doc, finalList);
    }

    // === UI ===
    var w = new Window("dialog", "Seleksi Alam");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 10;
    w.margins = 16;

    var panelCb = w.add("panel", undefined, "Daftar Layer (Ceklis = Hide)");
    panelCb.orientation = "column";
    panelCb.alignChildren = ["left", "top"];
    panelCb.spacing = 5;

    var groupCb = panelCb.add("group");
    groupCb.orientation = "row";
    groupCb.alignChildren = ["left", "top"];
    groupCb.spacing = 20;

    var col1 = groupCb.add("group");
    col1.orientation = "column";
    col1.alignChildren = ["left", "top"];

    var col2 = groupCb.add("group");
    col2.orientation = "column";
    col2.alignChildren = ["left", "top"];

    var cbRedaksi = col1.add("checkbox", undefined, "REDAKSI SEKOLAH");
    var cbAset = col1.add("checkbox", undefined, "ASET");
    var cbKolase = col1.add("checkbox", undefined, "KOLASE");
    var cbBgKolase = col1.add("checkbox", undefined, "BG KOLASE");
    var cbBg = col1.add("checkbox", undefined, "BG");

    var cbProp = col2.add("checkbox", undefined, "PROP");
    var cbKarpet = col2.add("checkbox", undefined, "KARPET");
    var cbUtama = col2.add("checkbox", undefined, "UTAMA");
    var cbOrtuLogic = col2.add("checkbox", undefined, "Logika Ortu (Layer Anak)");

    var pnlPreset = w.add("panel", undefined, "Preset Cepat");
    pnlPreset.orientation = "row";
    pnlPreset.alignChildren = ["fill", "top"];
    var btnPresetAnak = pnlPreset.add("button", undefined, "ANAK SAJA");
    var btnPresetOrtu = pnlPreset.add("button", undefined, "ORTU SAJA");

    function setPreset(mode) {
        cbRedaksi.value = true;
        cbAset.value = true;
        cbBg.value = true;
        cbProp.value = true;
        cbKarpet.value = true;
        
        if (mode === 1) { // ANAK SAJA
            cbKolase.value = true;
            cbBgKolase.value = true;
            cbUtama.value = false;
            cbOrtuLogic.value = false;
        } else { // ORTU SAJA
            cbKolase.value = false;
            cbBgKolase.value = true;
            cbUtama.value = true;
            cbOrtuLogic.value = true;
        }
    }

    btnPresetAnak.onClick = function() { setPreset(1); };
    btnPresetOrtu.onClick = function() { setPreset(2); };

    var cbBatch = w.add("checkbox", undefined, "Batch (Semua File Terbuka)");
    cbBatch.alignment = "center";

    var grpAction = w.add("group");
    grpAction.alignment = "center";
    var btnProses = grpAction.add("button", undefined, "PROSES HIDE", {name: "ok"});
    var btnCancel = grpAction.add("button", undefined, "Cancel");

    // Handlers
    btnProses.onClick = function () { 
        var list = [];
        if (cbRedaksi.value) list.push("REDAKSI SEKOLAH");
        if (cbAset.value) list.push("ASET");
        if (cbKolase.value) list.push("KOLASE");
        if (cbBgKolase.value) list.push("BG KOLASE");
        if (cbBg.value) list.push("BG");
        if (cbProp.value) list.push("PROP");
        if (cbKarpet.value) { list.push("KARPET"); list.push("karpet"); }
        if (cbUtama.value) list.push("UTAMA");

        processDocument(list, cbOrtuLogic.value, cbBatch.value);
        w.close(); 
    };
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
