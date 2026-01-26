#target photoshop

    /*
        seleksi_alam.jsx
        Fitur: Menu Seleksi Layer Otomatis (Hide/Show specific layers)
        Modes:
        1. S-WS BARU
        2. S-WS BARU ORTU
        3. S-PROFESI
        4. S-PROFESI ORTU
    */

    (function () {
        if (app.documents.length === 0) {
            alert("Tidak ada dokumen yang terbuka!");
            return;
        }

        // === UTILS: Window Position Persistence ===
        function loadWindowLocation() {
            var f = new File(Folder.temp + "/bmachine_seleksi_alam_pos.json");
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
            var f = new File(Folder.temp + "/bmachine_seleksi_alam_pos.json");
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
            return null;
        }

        function hideLayers(doc, names) {
            for (var i = 0; i < names.length; i++) {
                var layer = findLayerRecursive(doc, names[i]);
                if (layer) layer.visible = false;
            }
        }

        function showAndSelectLayer(doc, name) {
            var layer = findLayerRecursive(doc, name);
            if (layer) {
                layer.visible = true;
                try { doc.activeLayer = layer; } catch (e) { }
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
            if (mode === 1) {
                // S-WS BARU
                // Hide: REDAKSI SEKOLAH, ASET, KECIL, BG KECIL, PROP, KARPET, BG
                var list = ["REDAKSI SEKOLAH", "ASET", "KECIL", "BG KECIL", "PROP", "KARPET", "BG"];
                hideLayers(doc, list);
            }
            else if (mode === 2) {
                // S-WS BARU ORTU
                // Hide: REDAKSI SEKOLAH, ASET, PROP, BESAR, BG KECIL, KARPET
                // Set/Show: KECIL
                var list = ["REDAKSI SEKOLAH", "ASET", "PROP", "BESAR", "BG KECIL", "KARPET"];
                hideLayers(doc, list);
                showAndSelectLayer(doc, "KECIL");
            }
            else if (mode === 3) {
                // S-PROFESI
                // Hide: REDAKSI SEKOLAH, ASET, KECIL, BG
                var list = ["REDAKSI SEKOLAH", "ASET", "KECIL", "BG"];
                hideLayers(doc, list);
            }
            else if (mode === 4) {
                // S-PROFESI ORTU
                // Hide: REDAKSI SEKOLAH, ASET, KECIL, BG, BG ORTU, KECIL ORTU
                var list = ["REDAKSI SEKOLAH", "ASET", "KECIL", "BG", "BG ORTU", "KECIL ORTU"];
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

        // Kolom 1
        var col1 = grp.add("group");
        col1.orientation = "column";
        var btn1 = col1.add("button", [0, 0, 140, 40], "S-WS BARU");
        var btn3 = col1.add("button", [0, 0, 140, 40], "S-PROFESI");

        // Kolom 2
        var col2 = grp.add("group");
        col2.orientation = "column";
        var btn2 = col2.add("button", [0, 0, 140, 40], "S-WS BARU ORTU");
        var btn4 = col2.add("button", [0, 0, 140, 40], "S-PROFESI ORTU");

        // Batch Checkbox
        var cbBatch = w.add("checkbox", undefined, "Batch (Semua File Terbuka)");
        cbBatch.alignment = "center";

        var btnCancel = w.add("button", undefined, "Cancel");

        // Handlers
        btn1.onClick = function () { processDocument(1, cbBatch.value); w.close(); };
        btn2.onClick = function () { processDocument(2, cbBatch.value); w.close(); };
        btn3.onClick = function () { processDocument(3, cbBatch.value); w.close(); };
        btn4.onClick = function () { processDocument(4, cbBatch.value); w.close(); };
        btnCancel.onClick = function () { w.close(); };

        // Restore Position
        var savedLoc = loadWindowLocation();
        if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
        else w.center();

        // Show
        var res = w.show();
        if (res != 2) { // Not Cancel logic (standard cancel returns 2 sometimes depending on ESC)
            saveWindowLocation(w.location);
        }
    })();
