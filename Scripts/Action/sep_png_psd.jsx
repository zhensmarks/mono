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
            var doc = app.documents.getByName(docNames[j]);
            app.activeDocument = doc;

            if (!doc.path) {
                failList.push(doc.name + " (error: file can pernah disave, path null)");
                continue;
            }

            var docPath = doc.path;
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // === Save PSD (replace template) ===
            var psdFile = new File(docPath + "/" + baseName + ".psd");
            var psdOptions = new PhotoshopSaveOptions();
            psdOptions.embedColorProfile = true;
            psdOptions.layers = true;
            psdOptions.maximizeCompatibility = true;
            doc.saveAs(psdFile, psdOptions, true, Extension.LOWERCASE);

            // === Duplikat dokumen dulu untuk PNG ===
            var dupDoc = doc.duplicate(baseName + "_forPNG");
            app.activeDocument = dupDoc;

            // === Select all layer + mergeVisible (Stamp Visible) ===
            executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);

            // === Jalankan action anti ramijud (set: bima) ===
            app.doAction("anti ramijud", "starter pack");

            // === Save PNG transparan kualitas maksimal ===
            var pngFile = new File(docPath + "/" + baseName + ".png");
            var pngSaveOptions = new PNGSaveOptions();
            dupDoc.saveAs(pngFile, pngSaveOptions, true, Extension.LOWERCASE);

            // === Tutup duplikat tanpa save ===
            dupDoc.close(SaveOptions.DONOTSAVECHANGES);

            // === Tutup dokumen aslinya juga ===
            doc.close(SaveOptions.DONOTSAVECHANGES);

            successList.push(baseName);
            $.writeln("Berhasil save PSD & PNG + close: " + baseName);
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
