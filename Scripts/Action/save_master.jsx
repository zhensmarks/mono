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

    // --- PANEL: STANDARD OUTPUT (3 Columns) ---
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

    // Pas Foto
    var btnPas2x3 = col2.add("button", undefined, "PAS FOTO (2x3)");
    btnPas2x3.preferredSize.width = 140;
    var btnPas4x6 = col2.add("button", undefined, "PAS FOTO (4x6)");
    btnPas4x6.preferredSize.width = 140;
    var btnPasKombi = col2.add("button", undefined, "PAS FOTO (KOMBI)");
    btnPasKombi.preferredSize.width = 140;

    // Separator small
    var sep = col2.add("panel");
    sep.alignment = "fill";
    sep.preferredSize.height = 1;

    // PNG
    var btnPng = col2.add("button", undefined, "PNG Standard");
    btnPng.preferredSize.width = 140;

    // Col 3: ARTBOARD
    var col3 = pnlStd.add("group");
    col3.orientation = "column";
    col3.add("statictext", undefined, "--- ARTBOARD ---");
    
    var chkLayer1 = col3.add("checkbox", undefined, "LAYER 1");
    var chkLayer2 = col3.add("checkbox", undefined, "LAYER 2");
    var chkLayer3 = col3.add("checkbox", undefined, "LAYER 3");
    chkLayer1.value = true;
    chkLayer2.value = false;
    chkLayer3.value = false;

    var btnArtJpg = col3.add("button", undefined, "PROSES ART");
    btnArtJpg.preferredSize.width = 140;
    btnArtJpg.helpTip = "Export Artboard ke JPG untuk semua file (Batch)";

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
    var btnJpgFolder = grpCustomBtns.add("button", undefined, "JPG");
    var btnPngFolder = grpCustomBtns.add("button", undefined, "PNG");
    btnJpgFolder.preferredSize.width = 130;
    btnPngFolder.preferredSize.width = 130;

    var grpCustomBtns2 = pnlCustom.add("group");
    grpCustomBtns2.orientation = "row";
    grpCustomBtns2.alignChildren = ["center", "center"];
    var btnJpgFolderOpen = grpCustomBtns2.add("button", undefined, "JPG TETAP");
    var btnPngFolderOpen = grpCustomBtns2.add("button", undefined, "PNG TETAP");
    btnJpgFolderOpen.preferredSize.width = 130;
    btnPngFolderOpen.preferredSize.width = 130;


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
    
    btnArtJpg.onClick = function () { dlg.close(401); };

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

    btnJpgFolderOpen.onClick = function () {
        if (txtCustomPath.text === "") { alert("Pilih folder tujuan dulu!"); return; }
        if (!new Folder(txtCustomPath.text).exists) { alert("Folder tidak ditemukan!"); return; }
        dlg.close(9);
    };

    btnPngFolderOpen.onClick = function () {
        if (txtCustomPath.text === "") { alert("Pilih folder tujuan dulu!"); return; }
        if (!new Folder(txtCustomPath.text).exists) { alert("Folder tidak ditemukan!"); return; }
        dlg.close(10);
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

    // --- SPECIAL MODE: ARTBOARD (401) ---
    if (choice == 401) {
        var isPng = false;
        
        var docs = [];
        for (var i = 0; i < app.documents.length; i++) {
            docs.push(app.documents[i]);
        }
        
        var totalSuccess = 0;
        var totalFail = 0;
        var allDetails = [];

        var repeatByName = {
            "LAYER 1": chkLayer1.value,
            "LAYER 2": chkLayer2.value,
            "LAYER 3": chkLayer3.value
        };
        var exportedOnce = {};
        var exportSchedules = [];

        function normalizeArtboardName(name) {
            return String(name || "").replace(/^\s+|\s+$/g, "").toUpperCase();
        }

        // Build schedule
        for (var i = 0; i < docs.length; i++) {
            var schedule = [];
            var doc = docs[i];
            try {
                app.activeDocument = doc;
                var artboardsInfo = getArtboardsData();
                
                for (var scheduleIdx = 0; scheduleIdx < artboardsInfo.length; scheduleIdx++) {
                    var normalizedName = normalizeArtboardName(artboardsInfo[scheduleIdx].name);
                    var isControlledLayer = repeatByName.hasOwnProperty(normalizedName);
                    var shouldExport = true;

                    if (isControlledLayer && !repeatByName[normalizedName]) {
                        if (exportedOnce[normalizedName]) {
                            shouldExport = false;
                        } else {
                            exportedOnce[normalizedName] = true;
                        }
                    }

                    if (shouldExport) schedule.push(scheduleIdx);
                }
            } catch (e) {}
            exportSchedules.push(schedule);
        }

        // Execute schedule
        for (var i = 0; i < docs.length; i++) {
            var doc = docs[i];
            var currentSchedule = exportSchedules[i];
            try {
                app.activeDocument = doc;
                
                if (!doc.path) {
                    allDetails.push(doc.name + " (Gagal: Belum disave/tidak ada path)");
                    totalFail++;
                    continue;
                }
                
                // Simpan PSD original dulu
                doc.save();
                
                var res = exportArtboards(doc, isPng, JPG_QUALITY, currentSchedule);
                totalSuccess += res.success;
                totalFail += res.fail;
                if (res.details.length > 0) {
                    // Beri prefix nama dokumen pada detail artboard
                    for (var d = 0; d < res.details.length; d++) {
                        allDetails.push("[" + doc.name + "] " + res.details[d]);
                    }
                }
                
                // Tutup dokumen setelah selesai diexport
                doc.close(SaveOptions.DONOTSAVECHANGES);
                
                if (res.cancelled) {
                    allDetails.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                    break;
                }
            } catch(e) {
                totalFail++;
                allDetails.push(doc.name + " (Error: " + e.message + ")");
                if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                    allDetails.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                    break;
                }
            }
        }
        
        var finalMsg = "Total Sukses: " + totalSuccess + ", Total Gagal: " + totalFail + "\n\n" + allDetails.join("\n");
        showScrollableAlert("Laporan Export Artboard (Batch)", finalMsg);
        return; // Stop di sini
    }

    var pasModeSub = 0;
    if (choice >= 300) {
        pasModeSub = choice - 300;
        choice = 3;
    }

    var INIT_MODE_JPG = (choice == 1 || choice == 5 || choice == 6 || choice == 7 || choice == 9);
    var INIT_MODE_KEEP_OPEN = (choice == 5 || choice == 9 || choice == 10);
    var INIT_MODE_ONLY_JPG = (choice == 6);
    var INIT_MODE_JPG_FOLDER = (choice == 7 || choice == 9);
    var INIT_MODE_PNG_FOLDER = (choice == 8 || choice == 10);
    var INIT_MODE_PNG = (choice == 2 || INIT_MODE_PNG_FOLDER);
    var INIT_MODE_PAS = (choice == 3);
    var MODE_SMART = (choice == 99);

    var customTargetFolder = null;
    if (INIT_MODE_JPG_FOLDER || INIT_MODE_PNG_FOLDER) {
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
            var MODE_JPG = INIT_MODE_JPG;
            var MODE_KEEP_OPEN = INIT_MODE_KEEP_OPEN;
            var MODE_ONLY_JPG = INIT_MODE_ONLY_JPG;
            var MODE_JPG_FOLDER = INIT_MODE_JPG_FOLDER;
            var MODE_PNG_FOLDER = INIT_MODE_PNG_FOLDER;
            var MODE_PNG = INIT_MODE_PNG;
            var MODE_PAS = INIT_MODE_PAS;

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
                var has2x3 = new Folder(docPath + "/2x3").exists;
                var has4x6 = new Folder(docPath + "/4x6").exists;

                if (has2x3 || has4x6) {
                    MODE_PAS = true;
                    if (has2x3 && has4x6) {
                        pasModeSub = 4; // Kombi
                    } else if (has2x3) {
                        pasModeSub = 2; // 2x3
                    } else if (has4x6) {
                        pasModeSub = 3; // 4x6
                    }
                } else {
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

                // Kembalikan Document Asli Menjadi Aktif
                app.activeDocument = doc;

                // SPECIAL LOGIC: Jika Mode PNG TETAP (choice == 10), jalankan _kembalikan_seleksi.jsx
                if (choice == 10) {
                    try {
                        var scriptFile = new File(new File($.fileName).parent + "/_kembalikan_seleksi.jsx");
                        if (scriptFile.exists) {
                            $.evalFile(scriptFile);
                        }
                    } catch (err) {
                        // Gagal eksekusi script, lewati saja
                    }
                }

                successList.push(baseName + (MODE_PNG_FOLDER ? " (PNG Folder)" : " (PSD+PNG)"));
            }

            // C. Close Original
            if (!MODE_KEEP_OPEN) {
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }

        } catch (e) {
            failList.push((docs[j] ? docs[j].name : "Unknown") + " (Error: " + e.message + ")");
            // Jangan close PSD jika gagal, biarkan tetap terbuka
            
            // Deteksi jika user menekan ESC (User Cancelled)
            if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                failList.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                break; // Keluar dari loop dokumen
            }
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

function exportArtboards(sourceDoc, isPng, quality, schedule) {
    var successCount = 0;
    var failCount = 0;
    var details = [];
    var basePath = sourceDoc.path.fsName;
    var baseName = sourceDoc.name.replace(/\.[^\.]+$/, "");
    var cancelled = false;

    var artboardsInfo = getArtboardsData();
    if (artboardsInfo.length === 0) {
        details.push("Tidak ada Artboard yang ditemukan.");
        return { success: 0, fail: 1, details: details, cancelled: cancelled };
    }
    
    var listToProcess = [];
    if (schedule !== undefined && schedule !== null) {
        for (var s = 0; s < schedule.length; s++) {
            listToProcess.push(artboardsInfo[schedule[s]]);
        }
    } else {
        listToProcess = artboardsInfo;
    }

    for (var i = 0; i < listToProcess.length; i++) {
        var ab = listToProcess[i];
        var abName = ab.name;
        
        try {
            var abBounds = ab.bounds;
            
            // 1. Buat seleksi di koordinat artboard pada dokumen asli
            var region = [
                [abBounds[0], abBounds[1]], // left, top
                [abBounds[2], abBounds[1]], // right, top
                [abBounds[2], abBounds[3]], // right, bottom
                [abBounds[0], abBounds[3]]  // left, bottom
            ];
            
            app.activeDocument = sourceDoc;
            sourceDoc.selection.select(region);
            
            // 2. Copy Merged (Salin semua yang terlihat di area tersebut)
            try {
                sourceDoc.selection.copy(true);
            } catch(e) {
                // Jika kosong/blank, copy(true) akan error. Kita abaikan atau lempar error.
                throw new Error("Area artboard kosong atau tidak bisa di-copy.");
            }
            
            sourceDoc.selection.deselect();
            
            // 3. Buat dokumen baru dengan ukuran persis sama
            var w = abBounds[2] - abBounds[0];
            var h = abBounds[3] - abBounds[1];
            var newDoc = app.documents.add(UnitValue(w, "px"), UnitValue(h, "px"), sourceDoc.resolution, abName, NewDocumentMode.RGB);
            app.activeDocument = newDoc;
            
            // 4. Paste hasilnya
            newDoc.paste();
            
            // Flatten (karena hasil paste mungkin floating)
            newDoc.flatten();
            
            try { app.doAction("anti ramijud", "starter pack"); } catch (e) {}
            var safeName = abName.replace(new RegExp('[\\\\\\\\/:*?"<>|]', 'g'), "_");
            
            // Membuat folder khusus untuk masing-masing nama artboard
            var artboardFolder = new Folder(basePath + "/" + safeName);
            if (!artboardFolder.exists) {
                artboardFolder.create();
            }
            
            var outName = baseName + "_" + safeName;
            var targetPath = artboardFolder.fsName + "/" + outName;
            
            if (isPng) {
                savePNG(newDoc, targetPath + ".png");
            } else {
                saveJPG(newDoc, targetPath + ".jpg", quality);
            }
            
            newDoc.close(SaveOptions.DONOTSAVECHANGES);
            
            app.activeDocument = sourceDoc; // return focus
            
            successCount++;
            details.push(abName + " (Berhasil)");
        } catch (e) {
            failCount++;
            details.push(abName + " (Gagal: " + e.message + ")");
            // Try to close active doc if it's the duplicated one
            if (app.activeDocument !== sourceDoc) {
                try { app.activeDocument.close(SaveOptions.DONOTSAVECHANGES); } catch(ex){}
                app.activeDocument = sourceDoc;
            }
            
            // Cek apakah user menekan ESC (User Cancelled)
            if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                cancelled = true;
                break; // Keluar dari loop artboard
            }
        }
    }
    
    return { success: successCount, fail: failCount, details: details, cancelled: cancelled };
}

function selectLayerById(id) {
    var desc = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putIdentifier(charIDToTypeID("Lyr "), id);
    desc.putReference(charIDToTypeID("null"), ref);
    desc.putBoolean(charIDToTypeID("MkVs"), false);
    executeAction(charIDToTypeID("slct"), desc, DialogModes.NO);
}

function getArtboardsData() {
    var artboards = [];
    var doc = app.activeDocument;
    
    // Hanya iterasi layer di tingkat paling atas (root) untuk menghindari child layers
    for (var i = 0; i < doc.layers.length; i++) {
        var lyr = doc.layers[i];
        
        // Artboard di Photoshop DOM dibaca sebagai LayerSet (Group)
        if (lyr.typename === "LayerSet") {
            var isArtboard = false;
            var abBounds = [];
            try {
                var ref = new ActionReference();
                ref.putIdentifier(charIDToTypeID("Lyr "), lyr.id);
                var desc = executeActionGet(ref);
                
                // Cek apakah properti artboard ada
                if (desc.hasKey(stringIDToTypeID("artboard"))) {
                    var abDesc = desc.getObjectValue(stringIDToTypeID("artboard"));
                    var rect = abDesc.getObjectValue(stringIDToTypeID("artboardRect"));
                    var top = rect.getDouble(stringIDToTypeID("top"));
                    var left = rect.getDouble(stringIDToTypeID("left"));
                    var bottom = rect.getDouble(stringIDToTypeID("bottom"));
                    var right = rect.getDouble(stringIDToTypeID("right"));
                    
                    abBounds = [left, top, right, bottom];
                    isArtboard = true;
                } else if (desc.hasKey(stringIDToTypeID("artboardEnabled"))) {
                    // Fallback jika artboardRect tidak ditemukan dengan cara di atas
                    isArtboard = desc.getBoolean(stringIDToTypeID("artboardEnabled"));
                    if (isArtboard) {
                        // Gunakan bounds dari layer (meski kadang kurang akurat jika ada efek luar)
                        var b = lyr.bounds;
                        abBounds = [b[0].as("px"), b[1].as("px"), b[2].as("px"), b[3].as("px")];
                    }
                }
            } catch(e) {}
            
            if (isArtboard) {
                artboards.push({ name: lyr.name, id: lyr.id, bounds: abBounds });
            }
        }
    }
    return artboards;
}
