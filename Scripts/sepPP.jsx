// @target photoshop

// ====================================================================
// Konfigurasi & Konstanta
// ====================================================================
var RESOLUTION_PPCM = 300 / 2.54; // 300 pixels per inch, converted to pixels per cm
var JPG_QUALITY = 12; // Kualitas JPG (0-12)
var ACTION_SET = "starter pack";
var ACTION_NAME = "anti ramijud";

// ====================================================================
// Dialog Pilihan Mode
// ====================================================================
function showModeDialog() {
    var dialog = new Window("dialog", "Pilih Mode Proses");
    dialog.orientation = "column";
    dialog.alignChildren = ["center", "center"];

    var text = dialog.add("statictext", undefined, "Pilih mode pemrosesan:");
    text.justify = "center";

    var buttonGroup = dialog.add("group");
    buttonGroup.orientation = "column";
    buttonGroup.alignChildren = ["fill", "center"];

    var btnDefault = buttonGroup.add("button", undefined, "DEFAULT");
    var btn2x3 = buttonGroup.add("button", undefined, "HANYA 2x3");
    var btn4x6 = buttonGroup.add("button", undefined, "HANYA 4x6");
    var btnKombi = buttonGroup.add("button", undefined, "KOMBI (2x3 & 4x6)");

    var mode = "";

    btnDefault.onClick = function() { mode = "DEFAULT"; dialog.close(); };
    btn2x3.onClick = function() { mode = "2x3"; dialog.close(); };
    btn4x6.onClick = function() { mode = "4x6"; dialog.close(); };
    btnKombi.onClick = function() { mode = "KOMBI"; dialog.close(); };

    dialog.center();
    dialog.show();

    return mode;
}

// ====================================================================
// Fungsi Pembantu
// ====================================================================
function createFolderIfNotExist(folderPath) {
    var folder = new Folder(folderPath);
    if (!folder.exists) {
        folder.create();
    }
    return folder;
}

function saveJPG(doc, filePath) {
    var jpgOptions = new JPEGSaveOptions();
    jpgOptions.quality = JPG_QUALITY;
    jpgOptions.embedColorProfile = true;
    jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
    jpgOptions.scans = 3;
    doc.saveAs(new File(filePath), jpgOptions, true, Extension.LOWERCASE);
}

// --- FUNGSI BARU DARI SCRIPT SEBELUMNYA ---
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
        try { tempDoc.crop([left, top, right, bottom]); } catch(e2) {}
    }

    // Resize ke ukuran final
    tempDoc.resizeImage(UnitValue(widthCm, 'cm'), UnitValue(heightCm, 'cm'), tempDoc.resolution, ResampleMethod.BICUBIC);

    // Simpan JPG
    saveJPG(tempDoc, targetFolder + "/" + baseName + ".jpg");

    // Tutup duplikat
    tempDoc.close(SaveOptions.DONOTSAVECHANGES);
    return true;
}


// ====================================================================
// Fungsi Utama
// ====================================================================
function main() {
    if (app.documents.length === 0) {
        alert("Tidak ada file yang terbuka di Photoshop. Silakan buka file terlebih dahulu.");
        return;
    }

    var mode = showModeDialog();
    if (mode === "") { // User cancelled
        alert("Proses dibatalkan oleh pengguna.");
        return;
    }

    app.activeDocument.suspendHistory("Proses sepPP", "processDocuments('" + mode + "')");
}

function processDocuments(mode) {
    var docNames = [];
    for (var i = 0; i < app.documents.length; i++) {
        docNames.push(app.documents[i].name);
    }

    var report = "";
    var successCountDefault = 0;
    var failCountDefault = 0;
    var successCount2x3 = 0;
    var successCount4x6 = 0;

    // ====================================================================
    // Proses per dokumen
    // ====================================================================
    for (var j = 0; j < docNames.length; j++) {
        var doc = null;
        try {
            doc = app.documents.getByName(docNames[j]);
            app.activeDocument = doc;

            if (!doc.path) {
                alert("Peringatan: File '" + doc.name + "' belum pernah disimpan. Silakan simpan terlebih dahulu.");
                failCountDefault++;
                continue;
            }

            var baseOutputFolder = doc.path;
            var baseName = doc.name.replace(/\.[^.]+$/, "");

            // Tahap 1: Simpan Master (PSD/PSB)
            doc.save();

            // Tahap 2: Buat JPG utama
            var dupDoc = doc.duplicate(baseName + "_forJPG");
            app.activeDocument = dupDoc;
            dupDoc.flatten();
            try {
                app.doAction(ACTION_NAME, ACTION_SET);
            } catch (e) {
                $.writeln("Peringatan: Action '" + ACTION_NAME + "' tidak ditemukan atau gagal dijalankan.");
            }
            var jpgFilePath = baseOutputFolder + "/" + baseName + ".jpg";
            saveJPG(dupDoc, jpgFilePath);
            dupDoc.close(SaveOptions.DONOTSAVECHANGES);
            successCountDefault++;

            // Tahap 3: Buat versi crop dari master jika diminta
            if (mode === "2x3" || mode === "KOMBI") {
                var folder2x3 = createFolderIfNotExist(baseOutputFolder + "/2x3");
                if(createCroppedVersion(doc, folder2x3, 2, 3)) {
                    successCount2x3++;
                }
            }
            if (mode === "4x6" || mode === "KOMBI") {
                var folder4x6 = createFolderIfNotExist(baseOutputFolder + "/4x6");
                if(createCroppedVersion(doc, folder4x6, 4, 6)) {
                    successCount4x6++;
                }
            }

            // Tahap 4: Tutup dokumen asli
            doc.close(SaveOptions.DONOTSAVECHANGES);

        } catch (e) {
            $.writeln("Error saat memproses '" + docNames[j] + "': " + e.message);
            failCountDefault++;
            if (doc) {
                try { doc.close(SaveOptions.DONOTSAVECHANGES); } catch(e) {}
            }
        }
    }

    // ====================================================================
    // Laporan Final
    // ====================================================================
    report += "--- Laporan Proses ---\n";
    report += "File Master & JPG Utama: " + successCountDefault + " berhasil, " + failCountDefault + " gagal.\n";
    if (mode === "2x3" || mode === "KOMBI") {
        report += "Ukuran 2x3 dibuat: " + successCount2x3 + " file\n";
    }
    if (mode === "4x6" || mode === "KOMBI") {
        report += "Ukuran 4x6 dibuat: " + successCount4x6 + " file\n";
    }

    alert(report + "\n\nProses sepPP selesai.");
}

// Jalankan fungsi utama dengan error handling
try {
    main();
} catch (e) {
    alert("Terjadi kesalahan tak terduga:\n" + e.message + "\nLine: " + e.line);
}
