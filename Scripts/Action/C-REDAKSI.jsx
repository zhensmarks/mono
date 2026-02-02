#target photoshop

    /*
        C-REDAKSI.jsx
        Fitur: Otomatisasi Redaksi & Seleksi Warna
        1. HIDE REDAKSI: Sembunyi Redaksi
        2. REDAKSI: Ganti Redaksi
        
        Options:
        - MANASIK: Tampilkan ONTA + Transform
        - COLOR RANGE: Action "COLORRANGE" set "ABENG"
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

        function playAction(actionName, setName) {
            try {
                var idPly = charIDToTypeID("Ply ");
                var desc = new ActionDescriptor();
                var ref = new ActionReference();
                ref.putName(charIDToTypeID("Actn"), actionName);
                ref.putName(charIDToTypeID("ASet"), setName);
                desc.putReference(charIDToTypeID("null"), ref);
                executeAction(idPly, desc, DialogModes.NO);
            } catch (e) {
                // Silently fail or alert if needed. 
            }
        }

        // === TRANSFORM FUNCTIONS ===
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
        function processAction(mode, isBatch, useColorRange, useManasik) {
            if (isBatch) {
                for (var i = 0; i < app.documents.length; i++) {
                    app.activeDocument = app.documents[i];
                    doRedaksiAction(app.activeDocument, mode, useColorRange, useManasik);
                }
            } else {
                doRedaksiAction(app.activeDocument, mode, useColorRange, useManasik);
            }
        }

        function doRedaksiAction(doc, mode, useColorRange, useManasik) {
            try {
                // Modes:
                // 2: HIDE REDAKSI
                // 3: REDAKSI (Replace)

                // MANASIK Option: Show ONTA + Transform
                if (useManasik) {
                    var onta = findLayerRecursive(doc, "ONTA");
                    if (onta) {
                        onta.visible = true;
                        doc.activeLayer = onta;
                        transformOnta();
                    }
                }

                // Handle REDAKSI SEKOLAH layer
                var redaksi = findLayerRecursive(doc, "REDAKSI SEKOLAH");
                if (redaksi) {
                    if (mode === 2) {
                        // HIDE REDAKSI
                        redaksi.visible = false;
                    } else if (mode === 3) {
                        // REDAKSI (Replace)
                        doc.activeLayer = redaksi;
                        redaksi.remove();
                        pasteInPlace();
                    }
                }

                // Execute COLOR RANGE Action if checkbox is checked
                if (useColorRange) {
                    playAction("COLORRANGE", "ABENG");
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
        var btnRedaksi = col1.add("button", [0, 0, 140, 40], "REDAKSI");

        // Kolom 2
        var col2 = grp.add("group");
        col2.orientation = "column";
        var btnHideRedaksi = col2.add("button", [0, 0, 140, 40], "HIDE REDAKSI");

        // Checkboxes Group
        var grpCheckboxes = w.add("panel", undefined, "Options");
        grpCheckboxes.orientation = "row";
        grpCheckboxes.alignChildren = ["left", "center"];
        grpCheckboxes.spacing = 15;
        grpCheckboxes.margins = 15;

        var cbBatch = grpCheckboxes.add("checkbox", undefined, "Batch All");
        var cbColorRange = grpCheckboxes.add("checkbox", undefined, "COLOR RANGE");
        var cbManasik = grpCheckboxes.add("checkbox", undefined, "MANASIK (Onta)");

        var btnCancel = w.add("button", undefined, "Cancel");

        // Handlers
        // Mode 3: REDAKSI (Replace)
        btnRedaksi.onClick = function () {
            processAction(3, cbBatch.value, cbColorRange.value, cbManasik.value);
            w.close();
        };

        // Mode 2: HIDE REDAKSI
        btnHideRedaksi.onClick = function () {
            processAction(2, cbBatch.value, cbColorRange.value, cbManasik.value);
            w.close();
        };

        btnCancel.onClick = function () { w.close(); };

        // Restore Position
        var savedLoc = loadWindowLocation();
        if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
        else w.center();

        var res = w.show();
        if (res != 2) saveWindowLocation(w.location);
    })();
