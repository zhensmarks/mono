#target photoshop

    (function () {
        if (app.documents.length === 0) {
            alert("Tidak ada dokumen yang terbuka!");
            return;
        }

        // === CONFIG ===
        var FONT_NAME = "Geomatrix-Black";
        var FALLBACK_FONT = "Arial-Black";
        var MARGIN_X = 1.5;
        var MARGIN_Y = 0.7;
        var TEXT_SIZE = 6;

        // === UI ===
        var w = new Window("dialog", "Simpel Footnote");
        w.orientation = "column";
        w.alignChildren = ["fill", "top"];
        w.spacing = 15;
        w.margins = 20;

        // --- SECTION: SEKOLAH ---
        var pnlSekolah = w.add("panel", undefined, "Nama Sekolah");
        pnlSekolah.alignChildren = ["fill", "center"];
        var txtSekolah = pnlSekolah.add("edittext", undefined, "");
        txtSekolah.active = true;

        // --- SECTION: UKURAN (3 Kolom) ---
        var pnlUkuran = w.add("panel", undefined, "Pilih Ukuran");
        var grpUkuran = pnlUkuran.add("group");
        grpUkuran.orientation = "row";
        grpUkuran.spacing = 20;

        var rb8R = grpUkuran.add("radiobutton", undefined, "8R");
        var rb10RP = grpUkuran.add("radiobutton", undefined, "10RP");
        rb10RP.value = true;

        var grpLainnya = grpUkuran.add("group");
        var rbLainnya = grpLainnya.add("radiobutton", undefined, "Lainnya:");
        var drpLainnya = grpLainnya.add("dropdownlist", undefined, ["12RP", "16RP", "20RP", "24RP"]);
        drpLainnya.selection = 0;
        drpLainnya.enabled = false;

        rbLainnya.onClick = function () {
            drpLainnya.enabled = true;
            rb8R.value = false;
            rb10RP.value = false;
        };
        rb8R.onClick = rb10RP.onClick = function () {
            drpLainnya.enabled = false;
            rbLainnya.value = false;
        };

        // --- SECTION: WARNA & STROKE (3 Kolom) ---
        var pnlWarna = w.add("panel", undefined, "Warna & Style");
        var grpWarna = pnlWarna.add("group");
        grpWarna.orientation = "row";
        grpWarna.spacing = 30;

        var rbHitam = grpWarna.add("radiobutton", undefined, "HITAM");
        var rbPutih = grpWarna.add("radiobutton", undefined, "PUTIH");
        rbHitam.value = true;

        // --- TOMBOL ---
        var grpBtn = w.add("group");
        grpBtn.alignment = "center";
        var btnOk = grpBtn.add("button", undefined, "Buat", { name: "ok" });
        var btnAll = grpBtn.add("button", undefined, "Semua Dokumen");
        var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });

        // === LOGIC ===
        function applyFootnote(doc, text, isBlack) {
            var layer = doc.artLayers.add();
            layer.kind = LayerKind.TEXT;
            var ti = layer.textItem;
            ti.contents = text;
            ti.size = TEXT_SIZE;

            // font
            try { ti.font = FONT_NAME; } catch (e) {
                try { ti.font = FALLBACK_FONT; } catch (e2) { }
            }

            // Color
            var c = new SolidColor();
            if (isBlack) {
                c.rgb.red = 0; c.rgb.green = 0; c.rgb.blue = 0;
            } else {
                c.rgb.red = 255; c.rgb.green = 255; c.rgb.blue = 255;
            }
            ti.color = c;

            // Position
            ti.position = [MARGIN_X, doc.height - MARGIN_Y];
        }

        function process(targetAll) {
            var sekolah = txtSekolah.text.toUpperCase();
            if (sekolah === "") { alert("Isi nama sekolah!"); return; }

            var sizeStr = "";
            if (rb8R.value) sizeStr = "8R";
            else if (rb10RP.value) sizeStr = "10RP";
            else sizeStr = drpLainnya.selection.text;

            var fullText = "NON REG_" + sizeStr + "_" + sekolah;
            var isBlack = rbHitam.value;

            if (targetAll) {
                for (var i = 0; i < app.documents.length; i++) {
                    app.activeDocument = app.documents[i];
                    applyFootnote(app.activeDocument, fullText, isBlack);
                }
            } else {
                applyFootnote(app.activeDocument, fullText, isBlack);
            }
            w.close();
        }

        btnOk.onClick = function () { process(false); };
        btnAll.onClick = function () { process(true); };
        btnCancel.onClick = function () { w.close(); };

        w.center();
        w.show();
    })();
