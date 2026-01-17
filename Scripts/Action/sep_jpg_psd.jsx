// @target photoshop

(function () {
    if (app.documents.length == 0) {
        alert("Euweuh file nu kabuka ngaco.");
        return;
    }

    // Clone list nama dokumen di awal
    var docNames = [];
    for (var i = 0; i < app.documents.length; i++) {
        docNames.push(app.documents[i].name);
    }

    var successList = [];
    var failList = [];

    // === Progress Reporting Helper ===
    function reportProgress(current, total, filename) {
        var f = new File(Folder.temp + "/bmachine_progress.json");
        f.open("w");
        f.write('{"current": ' + current + ', "total": ' + total + ', "file": "' + filename + '", "status": "processing"}');
        f.close();
    }

    for (var j = 0; j < docNames.length; j++) {
        // Report Progress
        reportProgress(j + 1, docNames.length, docNames[j]);
        try {
            // Cari dokumen by name
            var doc = app.documents.getByName(docNames[j]);
            app.activeDocument = doc;

            if (!doc.path) {
                failList.push(doc.name + " (error: file can pernah disave, path null)");
                continue;
            }

            var docPath = doc.path;
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // === Save Original File (PSD or PSB) by simulating Ctrl+S ===
            doc.save();

            // === Duplikat dokumen dulu untuk JPG ===
            var dupDoc = doc.duplicate(baseName + "_forJPG");
            app.activeDocument = dupDoc;

            // === Flatten image supaya template PSD ga berubah ===
            dupDoc.flatten();

            // === Jalankan action anti ramijud (set: bima) ===
            app.doAction("anti ramijud", "starter pack");

            // === Save JPG kualitas 12 ===
            var jpgFile = new File(docPath + "/" + baseName + ".jpg");
            var jpgOptions = new JPEGSaveOptions();
            jpgOptions.quality = 12;
            jpgOptions.embedColorProfile = true;
            jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
            jpgOptions.scans = 3;
            dupDoc.saveAs(jpgFile, jpgOptions, true, Extension.LOWERCASE);

            // === Tutup duplikat tanpa save ===
            dupDoc.close(SaveOptions.DONOTSAVECHANGES);

            // === Tutup dokumen aslinya juga ===
            doc.close(SaveOptions.DONOTSAVECHANGES);

            successList.push(baseName);
            $.writeln("Berhasil save PSD & JPG + close: " + baseName);
        } catch (e) {
            failList.push(docNames[j] + " (error: " + e.toString() + ")");
            $.writeln("Aduh error: " + docNames[j] + " ? " + e.toString());
        }
    }

    var report = "Ikan hiu makan tomat, alhamdulillah tamat!\n";
    report += "Nu sukses (" + successList.length + "):\n" + successList.join(", ") + "\n\n";
    report += "Skip nu gagal (" + failList.length + "):\n" + failList.join("\n");

    alert(report);
    $.writeln(report);

})();
