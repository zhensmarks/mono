#target photoshop

    /*
        C-REDAKSI.jsx
        Fitur: Otomatisasi Redaksi & Seleksi Warna
        1. R-COLOR: Ganti Redaksi + Masking Selective Color (Invert)
        2. H-R-COLOR: Sembunyi Redaksi + Masking Selective Color (Invert)
        3. REDAKSI ONLY: Cuma Ganti Redaksi
        4. R-ONTA: Tampilkan ONTA + Transform + Ganti Redaksi + Masking
    */

    (function () {
        if (app.documents.length === 0) {
            alert("Tidak ada dokumen yang terbuka!");
            return;
        }

        // === UTILS: Window Position Persistence ===
        function loadWindowLocation() {
            var f = new File(Folder.temp + "/bmachine_credaksi_pos.json");
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
            var f = new File(Folder.temp + "/bmachine_credaksi_pos.json");
            f.open("w");
            f.write('{"x": ' + Math.round(loc.x) + ', "y": ' + Math.round(loc.y) + '}');
            f.close();
        }

        // === ACTION MANAGER HELPERS ===
        function findLayerRecursive(root, name) {
            try { return root.layers.getByName(name); } catch (e) { }
            for (var i = 0; i < root.layers.length; i++) {
                if (root.layers[i].typename == "LayerSet") {
                    var found = findLayerRecursive(root.layers[i], name);
                    if (found) return found;
                }
            }
            return null;
        }

        function pasteInPlace() {
            try {
                var idpast = charIDToTypeID("past");
                var desc = new ActionDescriptor();
                desc.putBoolean(stringIDToTypeID("inPlace"), true);
                desc.putEnumerated(charIDToTypeID("AntA"), charIDToTypeID("Annt"), charIDToTypeID("Anno"));
                executeAction(idpast, desc, DialogModes.NO);
            } catch (e) {
                try { app.activeDocument.paste(); } catch (err) { }
            }
        }

        function colorRangeSkinTones() {
            try {
                var idClrR = charIDToTypeID("ClrR");
                var desc = new ActionDescriptor();
                desc.putInteger(charIDToTypeID("Fzns"), 93);
                desc.putEnumerated(charIDToTypeID("Clrs"), charIDToTypeID("Clrs"), stringIDToTypeID("skinTones"));
                desc.putBoolean(stringIDToTypeID("detectFaces"), true);
                executeAction(idClrR, desc, DialogModes.NO@);
            } catch (e) { alert("Color Range Error: " + e); }
        }

        function invertMask() {
            try {
                var idInvr = charIDToTypeID("Invr");
                executeAction(idInvr, undefined, DialogModes.NO);
            } catch (e) { }
        }

        function selectLayerMask() {
            try {
                var idslct = charIDToTypeID("slct");
                var desc = new ActionDescriptor();
                var ref = new ActionReference();
                ref.putEnumerated(charIDToTypeID("Chnl"), charIDToTypeID("Chnl"), charIDToTypeID("Msk "));
                desc.putReference(charIDToTypeID("null"), ref);
                desc.putBoolean(charIDToTypeID("MkVs"), false);
                executeAction(idslct, desc, DialogModes.NO);
            } catch (e) { }
        }

        function transformOnta() {
            try {
                var idTrnf = charIDToTypeID("Trnf");
                var desc = new ActionDescriptor();
                var idnull = charIDToTypeID("null");
                var ref = new ActionReference();
                ref.putEnumerated(charIDToTypeID("Lyr "), charIDToTypeID("Ordn"), charIDToTypeID("Trgt"));
                desc.putReference(idnull, ref);
                desc.putEnumerated(charIDToTypeID("FTTr"), charIDToTypeID("FTTr"), charIDToTypeID("FTQu"));

                var descOffset = new ActionDescriptor();
                descOffset.putUnitDouble(charIDToTypeID("Hrzn"), charIDToTypeID("#Dist"), 0.66);
                descOffset.putUnitDouble(charIDToTypeID("Vrtc"), charIDToTypeID("#Dist"), -0.99);
                desc.putObject(charIDToTypeID("Offs"), charIDToTypeID("Ofst"), descOffset);

                desc.putUnitDouble(charIDToTypeID("Wdth"), charIDToTypeID("#Prc"), 113.6);
                desc.putUnitDouble(charIDToTypeID("Hght"), charIDToTypeID("#Prc"), 113.6);

                desc.putBoolean(stringIDToTypeID("linked"), true);
                desc.putEnumerated(charIDToTypeID("Intr"), charIDToTypeID("Intr"), charIDToTypeID("Bcbc"));
                executeAction(idTrnf, desc, DialogModes.NO);
            } catch (e) { }
        }

        // === PROCESS LOGIC ===
        function processAction(mode, isBatch) {
            if (isBatch) {
                for (var i = 0; i < app.documents.length; i++) {
                    app.activeDocument = app.documents[i];
                    doRedaksiAction(app.activeDocument, mode);
                }
            } else {
                doRedaksiAction(app.activeDocument, mode);
            }
        }

        function doRedaksiAction(doc, mode) {
            try {
                if (mode === 4) {
                    var onta = findLayerRecursive(doc, "ONTA");
                    if (onta) {
                        onta.visible = true;
                        doc.activeLayer = onta;
                        transformOnta();
                    }
                }

                var redaksi = findLayerRecursive(doc, "REDAKSI SEKOLAH");
                if (redaksi) {
                    if (mode === 2) {
                        redaksi.visible = false;
                    } else {
                        doc.activeLayer = redaksi;
                        redaksi.remove();
                        pasteInPlace();
                    }
                }

                if (mode === 1 || mode === 2 || mode === 4) {
                    var selective = findLayerRecursive(doc, "Selective Color 1");
                    if (selective) {
                        doc.activeLayer = selective;
                        selectLayerMask();
                        invertMask();
                        colorRangeSkinTones();
                    }
                }
            } catch (err) { alert("Main Error: " + err); }
        }

        // === UI ===
        var w = new Window("dialog", "Redaksi & Color");
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
        var btn1 = col1.add("button", [0, 0, 140, 40], "R-COLOR");
        var btn3 = col1.add("button", [0, 0, 140, 40], "REDAKSI ONLY");

        // Kolom 2
        var col2 = grp.add("group");
        col2.orientation = "column";
        var btn2 = col2.add("button", [0, 0, 140, 40], "H-R-COLOR");
        var btn4 = col2.add("button", [0, 0, 140, 40], "R-ONTA");

        // Batch Checkbox
        var cbBatch = w.add("checkbox", undefined, "Batch (Semua File Terbuka)");
        cbBatch.alignment = "center";

        var btnCancel = w.add("button", undefined, "Cancel");

        // Handlers
        btn1.onClick = function () { processAction(1, cbBatch.value); w.close(); };
        btn2.onClick = function () { processAction(2, cbBatch.value); w.close(); };
        btn3.onClick = function () { processAction(3, cbBatch.value); w.close(); };
        btn4.onClick = function () { processAction(4, cbBatch.value); w.close(); };
        btnCancel.onClick = function () { w.close(); };

        // Restore Position
        var savedLoc = loadWindowLocation();
        if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
        else w.center();

        var res = w.show();
        if (res != 2) saveWindowLocation(w.location);
    })();
