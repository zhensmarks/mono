// @target photoshop

/*
    unified_save_master.jsx
    Gabungan dari: sep_jpg_psd.jsx, sep_png_psd.jsx, dan sepPP.jsx
    Fitur: Dialog pilihan mode untuk simpan master + export format lain.
    Update: Added Loop & Back Navigation.
*/

(function () {
    // === KONFIGURASI ===
    var JPG_QUALITY = 12;

    // === UTILS: Progress & Result Reporting ===
    function reportProgress(current, total, filename) {
        var f = new File(Folder.temp + "/bmachine_progress.json");
        f.open("w");
        f.write('{"current": ' + current + ', "total": ' + total + ', "file": "' + filename + '", "status": "processing"}');
        f.close();
    }

    function writeResult(title, lines) {
        var f = new File(Folder.temp + "/bmachine_result.json");
        f.open("w");
        f.encoding = "UTF-8";
        var escaped = [];
        for (var i = 0; i < lines.length; i++) {
            // Escape backslashes and double quotes
            var esc = lines[i].replace(/\\/g, '\\\\').replace(/"/g, '\\"');
            escaped.push('"' + esc + '"');
        }
        f.write('{"type":"result","title":"' + title + '","lines":[' + escaped.join(',') + ']}');
        f.close();
    }

    // === UTILS: Photoshop Helpers ===
    function saveJPG(doc, filePath) {
        var jpgOptions = new JPEGSaveOptions();
        jpgOptions.quality = JPG_QUALITY;
        jpgOptions.embedColorProfile = true;
        jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
        jpgOptions.scans = 3;
        doc.saveAs(new File(filePath), jpgOptions, true, Extension.LOWERCASE);
    }

    function savePNG(doc, filePath) {
        var pngOptions = new PNGSaveOptions();
        // pngOptions.compression = 0; // Default is fine
        // pngOptions.interlaced = false; 
        doc.saveAs(new File(filePath), pngOptions, true, Extension.LOWERCASE);
    }

    function createFolderIfNotExist(folderPath) {
        var folder = new Folder(folderPath);
        if (!folder.exists) {
            folder.create();
        }
        return folder;
    }

    // Logic Crop Pas Foto (dari sepPP.jsx)
    function createCroppedVersion(sourceDoc, targetFolder, widthCm, heightCm) {
        var baseName = sourceDoc.name.replace(/\.[^.]+$/, "");
        var tempDoc = sourceDoc.duplicate();
        app.activeDocument = tempDoc;

        var curW = tempDoc.width.as('px');
        var curH = tempDoc.height.as('px');
        var targetRatio = widthCm / heightCm;

        var cropW, cropH;
        if (curW / curH > targetRatio) {
            cropH = curH;
            cropW = Math.round(curH * targetRatio);
        } else {
            cropW = curW;
            cropH = Math.round(curW / targetRatio);
        }

        var left = Math.round((curW - cropW) / 2);
        var top = Math.round((curH - cropH) / 2);
        var right = left + cropW;
        var bottom = top + cropH;

        try {
            tempDoc.crop([UnitValue(left, 'px'), UnitValue(top, 'px'), UnitValue(right, 'px'), UnitValue(bottom, 'px')]);
        } catch (e) {
            try { tempDoc.crop([left, top, right, bottom]); } catch (e2) { }
        }

        // Resize
        tempDoc.resizeImage(UnitValue(widthCm, 'cm'), UnitValue(heightCm, 'cm'), tempDoc.resolution, ResampleMethod.BICUBIC);

        // Save JPG
        saveJPG(tempDoc, targetFolder + "/" + baseName + ".jpg");

        tempDoc.close(SaveOptions.DONOTSAVECHANGES);
        return true;
    }

    // === DIALOG UTAMA ===
    function showMainDialog() {
        var dlg = new Window("dialog", "Save Master Options");
        dlg.orientation = "column";
        dlg.alignChildren = ["fill", "center"];

        var pnl = dlg.add("panel", undefined, "Pilih Output:");
        pnl.alignChildren = ["fill", "center"];

        // Button 1: PSD + JPG (Close)
        var btnJpg = pnl.add("button", undefined, "JPG (Close)");
        btnJpg.onClick = function () { dlg.close(1); };

        // Button 1b: PSD + JPG (Keep Open)
        var btnJpgOpen = pnl.add("button", undefined, "JPG (Keep Open)");
        btnJpgOpen.onClick = function () { dlg.close(5); };

        // Button 2: PSD + PNG
        var btnPng = pnl.add("button", undefined, "PNG");
        btnPng.onClick = function () { dlg.close(2); };

        // Button 3: Pas Foto
        var btnPas = pnl.add("button", undefined, "PAS FOTO (Crop)");
        btnPas.onClick = function () { dlg.close(3); };

        var btnCancel = dlg.add("button", undefined, "Cancel");
        btnCancel.onClick = function () { dlg.close(0); };

        dlg.center();
        return dlg.show();
    }

    // === DIALOG PAS FOTO (Sub-Menu) ===
    function showPasFotoDialog() {
        var dlg = new Window("dialog", "Opsi Pas Foto");
        dlg.orientation = "column";
        dlg.alignChildren = ["fill", "center"];

        var pnl = dlg.add("panel", undefined, "Ukuran:");
        pnl.alignChildren = ["fill", "center"];

        var btnDefault = pnl.add("button", undefined, "Hanya JPG (Default)");
        btnDefault.onClick = function () { dlg.close(1); };

        var btn2x3 = pnl.add("button", undefined, "Hanya 2x3");
        btn2x3.onClick = function () { dlg.close(2); };

        var btn4x6 = pnl.add("button", undefined, "Hanya 4x6");
        btn4x6.onClick = function () { dlg.close(3); };

        var btnKombi = pnl.add("button", undefined, "KOMBI (2x3 & 4x6)");
        btnKombi.onClick = function () { dlg.close(4); };

        // Back Button
        var btnBack = dlg.add("button", undefined, "<< Kembali");
        btnBack.onClick = function () { dlg.close(-1); };

        var btnCancel = dlg.add("button", undefined, "Cancel");
        btnCancel.onClick = function () { dlg.close(0); };

        dlg.center();
        return dlg.show();
    }


    // === MAIN LOGIC ===
    if (app.documents.length == 0) {
        // Tulis error ke result file agar user tau
        writeResult("Error", ["Tidak ada dokumen terbuka di Photoshop."]);
        return;
    }

    var choice = 0;
    var pasModeSub = 0;

    // Navigation Loop
    while (true) {
        // 1. Tampilkan Dialog Utama
        choice = showMainDialog();
        if (choice == 0) return; // Cancelled

        // 2. Cek Mode Pas Foto
        if (choice == 3) {
            pasModeSub = showPasFotoDialog();
            if (pasModeSub == 0) return; // Cancelled
            if (pasModeSub == -1) continue; // Back to Main Menu
        }

        // If valid choice (and not back), break loop and process
        break;
    }

    // 3. Siapkan Mode
    var MODE_JPG = (choice == 1 || choice == 5);
    var MODE_KEEP_OPEN = (choice == 5);
    var MODE_PNG = (choice == 2);
    var MODE_PAS = (choice == 3);

    // 4. Proses Dokumen
    var docNames = [];
    for (var i = 0; i < app.documents.length; i++) {
        docNames.push(app.documents[i].name);
    }

    var successList = [];
    var failList = [];
    var success2x3 = 0;
    var success4x6 = 0;

    for (var j = 0; j < docNames.length; j++) {
        reportProgress(j + 1, docNames.length, docNames[j]);
        var doc = null;
        try {
            doc = app.documents.getByName(docNames[j]);
            app.activeDocument = doc;

            if (!doc.path) {
                failList.push(doc.name + " (belum pernah disave)");
                continue;
            }

            var docPath = doc.path;
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // A. Save Master (PSD/PSB)
            doc.save();

            // B. Export Sesuai Mode
            if (MODE_JPG || MODE_PAS) {
                // Duplicate & Flatten
                var dupDoc = doc.duplicate(baseName + "_temp");
                app.activeDocument = dupDoc;
                dupDoc.flatten();

                // Action 'anti ramijud' (optional, try catch)
                try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }

                // --- KHUSUS PAS FOTO (Match sepPP.jsx logic) ---
                if (MODE_PAS) {
                    // 1. Selalu simpan JPG Utama dulu (sama seperti sepPP)
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg");
                    successList.push(baseName + " (JPG Utama)");

                    var baseOutputFolder = docPath;

                    // Close dupDoc (flattened version) karena crop harus dari MASTER
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    dupDoc = null;

                    // 2. Buat crop tambahan (dari doc master original)
                    if (pasModeSub == 2 || pasModeSub == 4) { // 2x3
                        var folder2x3 = createFolderIfNotExist(baseOutputFolder + "/2x3");
                        if (createCroppedVersion(doc, folder2x3, 2, 3)) success2x3++;
                    }
                    if (pasModeSub == 3 || pasModeSub == 4) { // 4x6
                        var folder4x6 = createFolderIfNotExist(baseOutputFolder + "/4x6");
                        if (createCroppedVersion(doc, folder4x6, 4, 6)) success4x6++;
                    }

                    if (pasModeSub != 1) { // Jika bukan "Hanya JPG", berarti ada crop
                        successList.push(baseName + " (+Crops)");
                    }

                } else if (MODE_JPG) {
                    // --- JPG BIASA (Standard) ---
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg");
                    successList.push(baseName + " (PSD+JPG)");
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                }
                // (MODE_PNG ada di else if berikutnya)

            } else if (MODE_PNG) {
                // Save PSD + PNG
                // Save PSD As (Make sure compatibility)
                var psdFile = new File(docPath + "/" + baseName + ".psd");
                var psdOptions = new PhotoshopSaveOptions();
                psdOptions.embedColorProfile = true;
                psdOptions.layers = true;
                psdOptions.maximizeCompatibility = true;
                doc.saveAs(psdFile, psdOptions, true, Extension.LOWERCASE);

                // Export PNG
                var dupDoc = doc.duplicate(baseName + "_forPNG");
                app.activeDocument = dupDoc;

                // Stamp Visible (Merge All)
                executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);

                try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }

                savePNG(dupDoc, docPath + "/" + baseName + ".png");
                dupDoc.close(SaveOptions.DONOTSAVECHANGES);

                successList.push(baseName + " (PSD+PNG)");
            }

            // C. Close Original
            if (!MODE_KEEP_OPEN) {
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }

        } catch (e) {
            failList.push(docNames[j] + " (Error: " + e.message + ")");
            if (doc) {
                try { doc.close(SaveOptions.DONOTSAVECHANGES); } catch (ex) { }
            }
        }
    }

    // 5. Laporan Final
    var modeLabel = "Unknown";
    if (MODE_JPG) modeLabel = "Save PSD + JPG" + (MODE_KEEP_OPEN ? " (Keep Open)" : " (Standard)");
    else if (MODE_PNG) modeLabel = "Save PSD + PNG (Standard)";
    else if (MODE_PAS) {
        modeLabel = "Pass Foto Output";
        var sizes = [];
        if (pasModeSub == 1) sizes.push("JPG Default");
        if (pasModeSub == 2 || pasModeSub == 4) sizes.push("2x3");
        if (pasModeSub == 3 || pasModeSub == 4) sizes.push("4x6");
        modeLabel += " " + sizes.join(" + ");
    }

    var reportLines = [];
    reportLines.push("Mode: " + modeLabel);
    reportLines.push("Total Dokumen: " + docNames.length);
    reportLines.push("Sukses: " + successList.length);
    reportLines.push("Gagal: " + failList.length);
    reportLines.push("");

    if (successList.length > 0) {
        reportLines.push("Detail Sukses:");
        for (var k = 0; k < successList.length; k++) reportLines.push("- " + successList[k]);
    }

    if (failList.length > 0) {
        reportLines.push("");
        reportLines.push("Detail Gagal:");
        for (var k = 0; k < failList.length; k++) reportLines.push("- " + failList[k]);
    }

    writeResult("Save Master Result", reportLines);

})();
