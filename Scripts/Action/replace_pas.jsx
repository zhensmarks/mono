
function extractNumberForSort(name) {
    var m = name.match(/\((\d+)\)/) || name.match(/(\d+)/);
    return m ? parseInt(m[1], 10) : Number.MAX_SAFE_INTEGER;
}
function naturalFileSort(a, b) {
    var na = extractNumberForSort(a.name);
    var nb = extractNumberForSort(b.name);
    if (na !== nb) return na - nb;
    var an = a.name.toLowerCase(), bn = b.name.toLowerCase();
    return an < bn ? -1 : an > bn ? 1 : 0;
}

// @target photoshop

(function () {
    // === BMachine Integration ===
    var bmachineContext = null;
    var tempFile = new File(Folder.temp + "/bmachine_context.json");
    if (tempFile.exists) {
        try {
            tempFile.open("r");
            var jsonContent = tempFile.read();
            tempFile.close();
            bmachineContext = eval("(" + jsonContent + ")");
        } catch (e) { }
    }

    var templateFolder = null;
    if (bmachineContext && bmachineContext.UseOutput && bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
        var path = bmachineContext.SourceFolders[0].OutputPath;
        if (path && new Folder(path).exists) templateFolder = new Folder(path);
    }
    if (!templateFolder) templateFolder = Folder.selectDialog("Pilih folder masternya dulu aja.");
    if (!templateFolder) { alert("Ga jadi ah males."); return; }

    var inputFolder = null;
    if (bmachineContext && bmachineContext.UseInput && bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
        var path = bmachineContext.SourceFolders[0].SourcePath;
        if (path && new Folder(path).exists) inputFolder = new Folder(path);
    }
    if (!inputFolder) inputFolder = Folder.selectDialog("Sekarang pilih folder seleksiannya.");
    if (!inputFolder) { alert("Ga jadi ah males."); return; }

    var templateFiles = scanFolderForFiles(templateFolder, /\.(psd|psb)$/i);
    templateFiles.sort(naturalFileSort);
    var inputFiles = scanFolderForFiles(inputFolder, /\.(jpg|png)$/i);

    if (templateFiles.length == 0 || inputFiles.length == 0) {
        alert("Masternya ga ada, seleksinya ga ada, parah sih.");
        return;
    }

    alert("Geus di pang ngecekeun yeuh\nMasterna aya: " + templateFiles.length + "\nSeleksina aya: " + inputFiles.length + " (png)\nLanjut gawean we ayeuna mah…");

    var successList = [];
    var failList = [];

    // === Progress Reporting Helper ===
    function reportProgress(current, total, filename) {
        var f = new File(Folder.temp + "/bmachine_progress.json");
        f.open("w");
        f.write('{"current": ' + current + ', "total": ' + total + ', "file": "' + filename + '", "status": "processing"}');
        f.close();
    }

    // === Loop Process ===
    for (var i = 0; i < templateFiles.length; i++) {
        // Report Progress
        reportProgress(i + 1, templateFiles.length, templateFiles[i].name);

        var template = templateFiles[i];
        var templateName = ("" + template.name).replace(/^\s+|\s+$/g, "");

        // Cari angka di dalam kurung dulu
        var templateNumberMatch = templateName.match(/\((\d+)\)/);
        if (!templateNumberMatch) {
            // Fallback: cari angka dimanapun
            templateNumberMatch = templateName.match(/(\d+)/);
        }

        if (!templateNumberMatch) {
            failList.push(relPath(templateFolder, template) + " (Rename masterna ngaco, euweuh angka)");
            continue;
        }

        var templateNumber = templateNumberMatch[1];

        var matchedInputs = [];
        for (var j = 0; j < inputFiles.length; j++) {
            var inputName = ("" + inputFiles[j].name).replace(/^\s+|\s+$/g, "");
            var inputNumberMatch = inputName.match(/\((\d+)\)/);
            if (!inputNumberMatch) {
                inputNumberMatch = inputName.match(/(\d+)/);
            }
            if (inputNumberMatch && inputNumberMatch[1] == templateNumber) {
                matchedInputs.push(inputFiles[j]);
            }
        }

        matchedInputs.sort(function (a, b) {
            var aIsPng = /\.png$/i.test(a.name);
            var bIsPng = /\.png$/i.test(b.name);
            if (aIsPng && !bIsPng) return -1;
            if (!aIsPng && bIsPng) return 1;
            return a.name.toLowerCase() < b.name.toLowerCase() ? -1 : 1;
        });

        if (matchedInputs.length == 0) {
            failList.push(relPath(templateFolder, template) + " (Henteu kapanggih inputna)");
            continue;
        }

        try {
            var doc = app.open(template);
            var smartXL = findSmartObject(doc, "XL");

            if (smartXL) {
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");
                successList.push(relPath(templateFolder, template));
            } else {
                failList.push(relPath(templateFolder, template) + " (Smart Object XL euweuh cuk)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }
        } catch (e) {
            failList.push(relPath(templateFolder, template) + " (error: " + e.toString() + ")");
        }
    }


    // === Step 5: Ringkasan mirip psdbucin.pyw ===
    var masterName = templateFiles.length > 0 ? templateFiles[0].name : "-";
    var masterExt = masterName.match(/\.[^.]+$/) ? masterName.match(/\.[^.]+$/)[0] : "-";
    var totalFile = templateFiles.length;
    var detail = [];
    for (var i = 0; i < successList.length; i++) {
        detail.push("OK     : " + successList[i]);
    }
    for (var i = 0; i < failList.length; i++) {
        detail.push("GAGAL  : " + failList[i]);
    }
    var report = [];
    report.push("Master  : " + masterName);
    report.push("Ekstensi: " + masterExt);
    report.push("Total file: " + totalFile);
    report.push("");
    report.push("Detail:");
    report = report.concat(detail);

    // === Tampilkan ringkasan dalam window custom scrollable ===
    var dlg = new Window("dialog", "Ringkasan Mirror");
    dlg.orientation = "column";
    dlg.alignChildren = "fill";
    dlg.preferredSize = [350, 350];
    dlg.minsize = [300, 250];

    dlg.add("statictext", undefined, "Ringkasan", { bold: true });
    var txt = dlg.add("edittext", undefined, report.join("\n"), { multiline: true, readonly: true, scrolling: true });
    txt.preferredSize = [330, 230];
    var btn = dlg.add("button", undefined, "OK", { name: "ok" });
    btn.alignment = "center";
    btn.onClick = function () { dlg.close(); };
    dlg.center();
    dlg.show();
    $.writeln(report.join("\n"));

    function scanFolderForFiles(folder, regex) {
        var files = [];
        var items = folder.getFiles();
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item instanceof File && item.name.match(regex)) {
                files.push(item);
            } else if (item instanceof Folder) {
                files = files.concat(scanFolderForFiles(item, regex));
            }
        }
        return files;
    }

    function findSmartObject(document, layerName) {
        for (var j = 0; j < document.artLayers.length; j++) {
            var layer = document.artLayers[j];
            if (layer.name == layerName && layer.kind == LayerKind.SMARTOBJECT) {
                return layer;
            }
        }
        for (var k = 0; k < document.layerSets.length; k++) {
            var group = document.layerSets[k];
            var found = searchInGroup(group, layerName);
            if (found) return found;
        }
        return null;
    }

    function searchInGroup(group, layerName) {
        for (var m = 0; m < group.artLayers.length; m++) {
            var l = group.artLayers[m];
            if (l.name == layerName && l.kind == LayerKind.SMARTOBJECT) {
                return l;
            }
        }
        for (var n = 0; n < group.layerSets.length; n++) {
            var g = group.layerSets[n];
            var found = searchInGroup(g, layerName);
            if (found) return found;
        }
        return null;
    }

    function replaceSmartContent(fileObj) {
        var id = stringIDToTypeID("placedLayerReplaceContents");
        var desc = new ActionDescriptor();
        desc.putPath(charIDToTypeID("null"), new File(fileObj));
        desc.putInteger(charIDToTypeID("PgNm"), 1);
        executeAction(id, desc, DialogModes.NO);
    }

    function relPath(rootFolder, file) {
        return decodeURI(file.fullName).replace(decodeURI(rootFolder.fullName) + "/", "");
    }

})();
