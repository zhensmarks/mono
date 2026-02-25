// @target photoshop

/*
    unified_save_master.jsx
    Fitur: Dialog pilihan mode untuk simpan master + export format lain.
    Update: Added Custom Folder UI & Window Persistence.
*/

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/save_master_settings.json");
    if (settingsFile.exists) {
        try {
            settingsFile.open("r");
            var content = settingsFile.read();
            settingsFile.close();
            return eval("(" + content + ")");
        } catch (e) { }
    }
    return { x: -1, y: -1 };
}

function saveSettings(x, y) {
    var settingsFile = new File(Folder.userData + "/save_master_settings.json");
    try {
        settingsFile.open("w");
        settingsFile.write('{"x":' + x + ',"y":' + y + '}');
        settingsFile.close();
    } catch (e) { }
}

function main() {
    // === KONFIGURASI ===
    var JPG_QUALITY = 12;

    // === MAIN LOGIC ===
    if (app.documents.length == 0) {
        alert("Tidak ada dokumen terbuka di Photoshop.");
        return;
    }

    var settings = loadSettings();

    // === DIALOG UTAMA ===
    var dlg = new Window("dialog", "Save Master Options", undefined, { borderless: true }); // Borderless removed white title bar
    dlg.orientation = "column";
    dlg.alignChildren = ["fill", "top"];
    dlg.spacing = 15;
    dlg.margins = 20;

    // --- PANEL: STANDARD OUTPUT (2 Columns) ---
    var pnlStd = dlg.add("panel", undefined, "Standard Output (Same Folder)");
    pnlStd.orientation = "row";
    pnlStd.alignChildren = ["fill", "top"];
    pnlStd.spacing = 20;
    pnlStd.margins = 15;

    // Col 1: JPG
    var col1 = pnlStd.add("group");
    col1.orientation = "column";
    col1.add("statictext", undefined, "--- JPG ---");
    var btnJpg = col1.add("button", undefined, "JPG - TUTUP");
    btnJpg.preferredSize.width = 140;

    var btnJpgOpen = col1.add("button", undefined, "JPG - TETAP");
    btnJpgOpen.preferredSize.width = 140;

    var btnSelectedJpg = col1.add("button", undefined, "TERPILIH");
    btnSelectedJpg.preferredSize.width = 140;

    // Separator small
    var sepJpg = col1.add("panel");
    sepJpg.alignment = "fill";
    sepJpg.preferredSize.height = 1;

    // --- SMART SAVE BUTTON ---
    var btnSmartSave = col1.add("button", undefined, "SAVE ORIGINAL (AUTO)");
    btnSmartSave.preferredSize.width = 140;
    btnSmartSave.helpTip = "Otomatis update JPG/PNG yang sudah ada. Jika tidak ada, hanya Save PSD.";

    // Col 2: PNG & PAS FOTO (Merged)
    var col2 = pnlStd.add("group");
    col2.orientation = "column";
    col2.add("statictext", undefined, "--- PNG & PAS FOTO ---");

    // PNG
    var btnPng = col2.add("button", undefined, "PNG Standard");
    btnPng.preferredSize.width = 140;

    // Separator small
    var sep = col2.add("panel");
    sep.alignment = "fill";
    sep.preferredSize.height = 1;

    // Pas Foto
    var btnPas2x3 = col2.add("button", undefined, "PAS FOTO (2x3)");
    btnPas2x3.preferredSize.width = 140;
    var btnPas4x6 = col2.add("button", undefined, "PAS FOTO (4x6)");
    btnPas4x6.preferredSize.width = 140;
    var btnPasKombi = col2.add("button", undefined, "PAS FOTO (KOMBI)");
    btnPasKombi.preferredSize.width = 140;

    // --- PANEL: CUSTOM FOLDER OUTPUT ---
    var pnlCustom = dlg.add("panel", undefined, "Custom Folder Output");
    pnlCustom.orientation = "column";
    pnlCustom.alignChildren = ["fill", "top"];
    pnlCustom.spacing = 10;
    pnlCustom.margins = 15;

    var grpPath = pnlCustom.add("group");
    grpPath.orientation = "row";
    grpPath.alignChildren = ["fill", "center"];

    var txtCustomPath = grpPath.add("edittext", undefined, "");
    txtCustomPath.preferredSize.width = 250;

    var btnClearPath = grpPath.add("button", undefined, "X");
    btnClearPath.size = [25, 25];

    var btnBrowsePath = grpPath.add("button", undefined, "Browse...");
    btnBrowsePath.preferredSize.width = 70;

    var grpCustomBtns = pnlCustom.add("group");
    grpCustomBtns.orientation = "row";
    grpCustomBtns.alignChildren = ["center", "center"];
    var btnJpgFolder = grpCustomBtns.add("button", undefined, "JPG to Folder");
    var btnPngFolder = grpCustomBtns.add("button", undefined, "PNG to Folder");
    btnJpgFolder.preferredSize.width = 130;
    btnPngFolder.preferredSize.width = 130;


    // --- BOTTOM ---
    var btnCancel = dlg.add("button", undefined, "Cancel");
    btnCancel.alignment = "center";

    // --- EVENTS ---
    btnJpg.onClick = function () { dlg.close(1); };
    btnJpgOpen.onClick = function () { dlg.close(5); };
    btnSelectedJpg.onClick = function () { dlg.close(100); }; // Code 100 for Selected JPG
    btnPng.onClick = function () { dlg.close(2); };

    // Pas Foto (Kode 301 dihapus karena redundant dengan JPG Biasa)
    btnPas2x3.onClick = function () { dlg.close(302); };
    btnPas4x6.onClick = function () { dlg.close(303); };
    btnPasKombi.onClick = function () { dlg.close(304); };

    btnSmartSave.onClick = function () { dlg.close(99); }; // Code 99 for Smart Save

    // Custom Path Events
    btnBrowsePath.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Output");
        if (f) txtCustomPath.text = decodeURI(f.fullName);
    };

    btnClearPath.onClick = function () {
        txtCustomPath.text = "";
        txtCustomPath.active = true;
    };

    btnJpgFolder.onClick = function () {
        if (txtCustomPath.text === "") { alert("Pilih folder tujuan dulu!"); return; }
        if (!new Folder(txtCustomPath.text).exists) { alert("Folder tidak ditemukan!"); return; }
        dlg.close(7);
    };

    btnPngFolder.onClick = function () {
        if (txtCustomPath.text === "") { alert("Pilih folder tujuan dulu!"); return; }
        if (!new Folder(txtCustomPath.text).exists) { alert("Folder tidak ditemukan!"); return; }
        dlg.close(8);
    };

    btnCancel.onClick = function () { dlg.close(0); };

    // Restore Location
    if (settings.x > 0 && settings.y > 0) {
        dlg.location = [settings.x, settings.y];
    } else {
        dlg.center();
    }

    var choice = dlg.show();

    // Save Location
    saveSettings(dlg.location.x, dlg.location.y);

    if (choice == 0) return; // Cancelled

    // === PROCESSING LOGIC Setup ===

    // --- SPECIAL MODE: SELECTED JPG (100) ---
    if (choice == 100) {
        // Show Document Selection Dialog
        var selDlg = new Window("dialog", "Pilih Dokumen untuk di-Save & Close");
        selDlg.orientation = "column";
        selDlg.alignChildren = ["fill", "fill"];

        var listGrp = selDlg.add("group");
        listGrp.orientation = "column";
        listGrp.alignChildren = ["fill", "fill"];
        listGrp.preferredSize = [400, 300];

        var lstDocs = listGrp.add("listbox", undefined, [], { multiselect: true });
        lstDocs.preferredSize = [380, 280];

        // Populate
        var allDocs = [];
        for (var i = 0; i < app.documents.length; i++) {
            allDocs.push(app.documents[i]);
            lstDocs.add("item", app.documents[i].name);
        }

        // Select all by default? Or none? Let's select all for convenience
        for (var k = 0; k < lstDocs.items.length; k++) {
            lstDocs.items[k].selected = true;
        }

        var btnGrp = selDlg.add("group");
        btnGrp.orientation = "row";
        btnGrp.alignment = "center";
        var btnProcessJpg = btnGrp.add("button", undefined, "JPG");
        var btnProcessPng = btnGrp.add("button", undefined, "PNG");
        var btnSelCancel = btnGrp.add("button", undefined, "CANCEL");

        btnProcessJpg.onClick = function () { selDlg.close(1); };
        btnProcessPng.onClick = function () { selDlg.close(2); };
        btnSelCancel.onClick = function () { selDlg.close(0); };

        var selChoice = selDlg.show();
        if (selChoice == 1 || selChoice == 2) {
            // Process Selected Only
            var selectedIndices = [];
            for (var k = 0; k < lstDocs.items.length; k++) {
                if (lstDocs.items[k].selected) {
                    selectedIndices.push(k);
                }
            }

            if (selectedIndices.length == 0) return;

            var successList = [];
            var failList = [];
            // Iterate selected. 

            var docsToProcess = [];
            for (var m = 0; m < selectedIndices.length; m++) {
                docsToProcess.push(allDocs[selectedIndices[m]]);
            }

            for (var d = 0; d < docsToProcess.length; d++) {
                var docName = docsToProcess[d] ? docsToProcess[d].name : "Unknown Document";
                try {
                    var doc = docsToProcess[d];
                    app.activeDocument = doc; // Activate

                    if (!doc.path) {
                        failList.push(docName + " (Belum disave/tidak ada path)");
                        continue; // Skip unsaved
                    }

                    var baseName = doc.name.replace(/\.[^\.]+$/, "");
                    var docPath = doc.path;

                    // 1. Save PSD
                    doc.save();

                    // 2. Export
                    if (selChoice == 1) {
                        var dupDoc = doc.duplicate(baseName + "_temp");
                        app.activeDocument = dupDoc;
                        dupDoc.artLayers.add();
                        dupDoc.flatten();

                        try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }

                        saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                        dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    } else if (selChoice == 2) {
                        var dupDoc = doc.duplicate(baseName + "_forPNG");
                        app.activeDocument = dupDoc;
                        dupDoc.artLayers.add();
                        executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);

                        try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }

                        savePNG(dupDoc, docPath + "/" + baseName + ".png");
                        dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    }

                    // 3. Close Original
                    doc.close(SaveOptions.DONOTSAVECHANGES);
                    successList.push(docName);

                } catch (e) {
                    failList.push(docName + " (Error: " + e.message + ")");
                }
            }
            var msg = "Sukses: " + successList.length + "\n";
            if (successList.length > 0) msg += successList.join("\n") + "\n\n";
            msg += "Gagal: " + failList.length + "\n";
            if (failList.length > 0) msg += failList.join("\n");

            showScrollableAlert("Laporan Proses Dokumen Terpilih", msg);
        }
        return; // Exit main
    }

    var pasModeSub = 0;
    if (choice >= 300) {
        pasModeSub = choice - 300;
        choice = 3;
    }

    var MODE_JPG = (choice == 1 || choice == 5 || choice == 6 || choice == 7);
    var MODE_KEEP_OPEN = (choice == 5);
    var MODE_ONLY_JPG = (choice == 6);
    var MODE_JPG_FOLDER = (choice == 7);
    var MODE_PNG_FOLDER = (choice == 8);
    var MODE_PNG = (choice == 2 || MODE_PNG_FOLDER);
    var MODE_PAS = (choice == 3);
    var MODE_SMART = (choice == 99);

    var customTargetFolder = null;
    if (MODE_JPG_FOLDER || MODE_PNG_FOLDER) {
        customTargetFolder = new Folder(txtCustomPath.text);
    }

    // 4. Proses Dokumen
    var docs = [];
    for (var i = 0; i < app.documents.length; i++) {
        docs.push(app.documents[i]);
    }

    var successList = [];
    var failList = [];
    var success2x3 = 0;
    var success4x6 = 0;

    for (var j = 0; j < docs.length; j++) {
        var doc = null;
        try {
            doc = docs[j];
            app.activeDocument = doc;

            if (!doc.path && !customTargetFolder) {
                failList.push(doc.name + " (belum pernah disave & no custom folder)");
                continue;
            }

            var docPath;
            if (customTargetFolder) {
                try {
                    // Coba ambil path dokumen asal untuk mirroring folder
                    if (doc.path) {
                        var parentFolderName = doc.path.name;
                        var subFolder = new Folder(customTargetFolder + "/" + parentFolderName);
                        if (!subFolder.exists) subFolder.create();
                        docPath = subFolder.fsName;
                    } else {
                        docPath = customTargetFolder.fsName;
                    }
                } catch (e) {
                    // Fallback jika doc.path error (misal belum disave)
                    docPath = customTargetFolder.fsName;
                }
            } else {
                docPath = doc.path;
            }
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // --- SMART SAVE CHECK (Overrides Mode) ---
            if (MODE_SMART) {
                var jpgExists = new File(docPath + "/" + baseName + ".jpg").exists;
                var pngExists = new File(docPath + "/" + baseName + ".png").exists;

                if (jpgExists) {
                    MODE_JPG = true;
                } else if (pngExists) {
                    MODE_PNG = true;
                } else {
                    // Hanya Save PSD
                    doc.save();
                    doc.close(SaveOptions.DONOTSAVECHANGES);
                    successList.push(baseName + " (PSD Updated)");
                    continue; // Skip rest of loop
                }
            }

            // A. Save Master (PSD/PSB) - Skip if Only JPG/Folder mode
            if (!MODE_ONLY_JPG && !MODE_JPG_FOLDER && !MODE_PNG_FOLDER) {
                doc.save();
            }

            // B. Export Sesuai Mode
            if (MODE_JPG || MODE_PAS) {
                // Duplicate & Flatten
                var dupDoc = doc.duplicate(baseName + "_temp");
                app.activeDocument = dupDoc;

                // [FIX] Tambahkan layer baru (visible) sebelum flatten
                dupDoc.artLayers.add();
                dupDoc.flatten();

                // Action 'anti ramijud' (Skip if Only JPG/Folder mode)
                if (!MODE_ONLY_JPG && !MODE_JPG_FOLDER && !MODE_PNG_FOLDER) {
                    try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                }

                if (MODE_PAS) {
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                    successList.push(baseName + " (JPG Utama)");
                    var baseOutputFolder = docPath;
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    dupDoc = null;

                    if (pasModeSub == 2 || pasModeSub == 4) { // 2x3
                        var folder2x3 = createFolderIfNotExist(baseOutputFolder + "/2x3");
                        if (createCroppedVersion(doc, folder2x3, 2, 3, JPG_QUALITY)) success2x3++;
                    }
                    if (pasModeSub == 3 || pasModeSub == 4) { // 4x6
                        var folder4x6 = createFolderIfNotExist(baseOutputFolder + "/4x6");
                        if (createCroppedVersion(doc, folder4x6, 4, 6, JPG_QUALITY)) success4x6++;
                    }
                    if (pasModeSub != 1) successList.push(baseName + " (+Crops)");

                } else if (MODE_JPG) {
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                    successList.push(baseName + (MODE_JPG_FOLDER ? " (JPG Folder)" : " (PSD+JPG)"));
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                }

            } else if (MODE_PNG) {
                // Save PSD + PNG (Skip PSD if Folder mode)
                if (!MODE_PNG_FOLDER) {
                    var psdFile = new File(docPath + "/" + baseName + ".psd");
                    var psdOptions = new PhotoshopSaveOptions();
                    psdOptions.embedColorProfile = true;
                    psdOptions.layers = true;
                    psdOptions.maximizeCompatibility = true;
                    doc.saveAs(psdFile, psdOptions, true, Extension.LOWERCASE);
                }

                var dupDoc = doc.duplicate(baseName + "_forPNG");
                app.activeDocument = dupDoc;
                dupDoc.artLayers.add();
                executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);

                if (!MODE_PNG_FOLDER) {
                    try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                }

                savePNG(dupDoc, docPath + "/" + baseName + ".png");
                dupDoc.close(SaveOptions.DONOTSAVECHANGES);

                successList.push(baseName + (MODE_PNG_FOLDER ? " (PNG Folder)" : " (PSD+PNG)"));
            }

            // C. Close Original
            if (!MODE_KEEP_OPEN) {
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }

        } catch (e) {
            failList.push((docs[j] ? docs[j].name : "Unknown") + " (Error: " + e.message + ")");
            if (doc) { try { doc.close(SaveOptions.DONOTSAVECHANGES); } catch (ex) { } }
        }
    }

    // 5. Laporan Final
    var msg = "Sukses: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    showScrollableAlert("Laporan Simpan Dokumen", msg);
}

// Global invocation
main();

// === HELPERS ===
function saveJPG(doc, filePath, quality) {
    var jpgOptions = new JPEGSaveOptions();
    jpgOptions.quality = quality;
    jpgOptions.embedColorProfile = true;
    jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
    jpgOptions.scans = 3;
    doc.saveAs(new File(filePath), jpgOptions, true, Extension.LOWERCASE);
}

function savePNG(doc, filePath) {
    var pngOptions = new PNGSaveOptions();
    doc.saveAs(new File(filePath), pngOptions, true, Extension.LOWERCASE);
}

function createFolderIfNotExist(folderPath) {
    var folder = new Folder(folderPath);
    if (!folder.exists) folder.create();
    return folder;
}

function createCroppedVersion(sourceDoc, targetFolder, widthCm, heightCm, quality) {
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

    tempDoc.resizeImage(UnitValue(widthCm, 'cm'), UnitValue(heightCm, 'cm'), tempDoc.resolution, ResampleMethod.BICUBIC);
    saveJPG(tempDoc, targetFolder + "/" + baseName + ".jpg", quality);
    tempDoc.close(SaveOptions.DONOTSAVECHANGES);
    return true;
}

function showScrollableAlert(title, message) {
    var dialog = new Window("dialog", title);
    dialog.orientation = "column";
    dialog.alignChildren = ["fill", "fill"];
    dialog.preferredSize = [400, 300];

    var edittext = dialog.add("edittext", undefined, message, { multiline: true, scrolling: true, readonly: true });
    edittext.preferredSize = [380, 250];

    var btnOk = dialog.add("button", undefined, "OK");
    btnOk.alignment = "center";
    btnOk.onClick = function () { dialog.close(); };

    dialog.show();
}
