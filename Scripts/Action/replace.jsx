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
            // Simple eval for JSON (Safe as it comes from our app)
            bmachineContext = eval("(" + jsonContent + ")");
        } catch (e) { $.writeln("Json load error: " + e); }
    }

    // === Step 1: Pilih folder ===
    var templateFolder = null;

    // Check Config from BMachine for Template (Output Lokal)
    if (bmachineContext && bmachineContext.UseOutput && bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
        var path = bmachineContext.SourceFolders[0].OutputPath;
        if (path && new Folder(path).exists) {
            templateFolder = new Folder(path);
        }
    }

    if (!templateFolder) {
        templateFolder = Folder.selectDialog("Pilih folder masternya dulu aja.");
    }
    if (!templateFolder) { alert("Ga jadi ah males."); return; }

    var inputFolder = null;

    // Check Config from BMachine for Input (Pilihan)
    if (bmachineContext && bmachineContext.UseInput && bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
        var path = bmachineContext.SourceFolders[0].SourcePath;
        if (path && new Folder(path).exists) {
            inputFolder = new Folder(path);
        }
    }

    if (!inputFolder) {
        inputFolder = Folder.selectDialog("Sekarang pilih folder seleksiannya.");
    }
    if (!inputFolder) { alert("Ga jadi ah males."); return; }

    // === Step 2: Scan subfolder dan ambil file ===
    var templateFiles = scanFolderForFiles(templateFolder, /\.psd$/i);
    var allInputFiles = scanFolderForFiles(inputFolder, /\.(png|psd|jpe?g)$/i); // Scan semua jenis file

    // === Step 2.5: Tentukan mode operasi (PNG/PSD atau JPG) ===
    var hasPngPsd = false;
    for (var k = 0; k < allInputFiles.length; k++) {
        if (allInputFiles[k].name.match(/\.(png|psd)$/i)) {
            hasPngPsd = true;
            break;
        }
    }

    var inputFiles; // Ini akan jadi daftar file final untuk diproses
    var activeModeMessage;
    if (hasPngPsd) {
        // Mode Prioritas: Hanya proses PNG dan PSD
        inputFiles = [];
        for (var k = 0; k < allInputFiles.length; k++) {
            if (allInputFiles[k].name.match(/\.(png|psd)$/i)) {
                inputFiles.push(allInputFiles[k]);
            }
        }
        activeModeMessage = "Mode Prioritas: PNG/PSD ditemukan. Hanya file .png dan .psd yang akan diproses.";
    } else {
        // Mode Fallback: Hanya proses JPG karena tidak ada PNG/PSD
        inputFiles = allInputFiles; // Isinya pasti hanya JPG
        activeModeMessage = "Mode Fallback: Tidak ada PNG/PSD. Script akan memproses file .jpg.";
    }

    // Sort supaya urut angka terkecil ke terbesar
    templateFiles.sort(sortByNumberInFilename);
    inputFiles.sort(sortByNumberInFilename);

    var templateCount = templateFiles.length;
    var inputCount = inputFiles.length;

    if (templateCount == 0 || inputCount == 0) {
        alert("Masternya ga ada, seleksinya ga ada, parah sih.");
        return;
    }

    // === Step 3: Alert auto-check global ===
    alert("Geus di pang ngecekeun yeuh\nMasterna aya: " + templateCount + " termasuk subfolderna\nSeleksiana aya: " + inputCount + " file\n\n" + activeModeMessage + "\n\nLanjut gawean we ayeuna mah…");

    // === Step 3,5: Progress Config ===
    // (Dialog removed for BMachine Integration)

    // === Helper for BMachine Progress ===
    function reportProgress(current, total, filename) {
        var f = new File(Folder.temp + "/bmachine_progress.json");
        f.open("w");
        f.write('{"current": ' + current + ', "total": ' + total + ', "file": "' + filename + '", "status": "processing"}');
        f.close();
    }

    // === Step 4: Proses per template ===
    var successList = [];
    var failList = [];

    for (var i = 0; i < templateFiles.length; i++) {
        var template = templateFiles[i];

        // Report Progress to BMachine
        reportProgress(i + 1, templateCount, template.name);

        // === BARU: Logika pencocokan GABUNGAN (Nama dulu, baru Nomor) ===
        var matchedInputs = [];
        var templateRelDir = decodeURI(template.parent.fullName).replace(decodeURI(templateFolder.fullName), "");
        if (templateRelDir.indexOf("/") == 0) templateRelDir = templateRelDir.substring(1);

        // --- TAHAP 1: Coba cocokkan berdasarkan NAMA FILE SAMA PERSIS ---
        var templateBaseName = template.displayName.replace(/\.[^\.]+$/, "");
        if (templateBaseName) {
            var matchedByNameInSubfolder = [];
            var matchedByNameAnywhere = [];
            for (var j = 0; j < inputFiles.length; j++) {
                var input = inputFiles[j];
                var inputBaseName = input.displayName.replace(/\.[^\.]+$/, "");
                if (inputBaseName === templateBaseName) {
                    var inputRelDir = decodeURI(input.parent.fullName).replace(decodeURI(inputFolder.fullName), "");
                    if (inputRelDir.indexOf("/") == 0) inputRelDir = inputRelDir.substring(1);
                    if (inputRelDir == templateRelDir) {
                        matchedByNameInSubfolder.push(input);
                    } else {
                        matchedByNameAnywhere.push(input);
                    }
                }
            }
            if (matchedByNameInSubfolder.length > 0) {
                matchedInputs = matchedByNameInSubfolder;
            } else if (matchedByNameAnywhere.length > 0) {
                matchedInputs = matchedByNameAnywhere;
            }
        }

        // --- TAHAP 2: Jika tidak ketemu, coba cocokkan berdasarkan NOMOR ---
        if (matchedInputs.length === 0) {
            var templateNumberMatch = template.name.match(/(\d+)/);
            if (templateNumberMatch) {
                var templateNumber = templateNumberMatch[1];
                var matchedByNumInSubfolder = [];
                var matchedByNumAnywhere = [];
                for (var j = 0; j < inputFiles.length; j++) {
                    var input = inputFiles[j];
                    var inputNumberMatch = input.name.match(/(\d+)/);
                    if (inputNumberMatch && inputNumberMatch[1] == templateNumber) {
                        var inputRelDir = decodeURI(input.parent.fullName).replace(decodeURI(inputFolder.fullName), "");
                        if (inputRelDir.indexOf("/") == 0) inputRelDir = inputRelDir.substring(1);
                        if (inputRelDir == templateRelDir) {
                            matchedByNumInSubfolder.push(input);
                        } else {
                            matchedByNumAnywhere.push(input);
                        }
                    }
                }
                if (matchedByNumInSubfolder.length > 0) {
                    matchedInputs = matchedByNumInSubfolder;
                } else if (matchedByNumAnywhere.length > 0) {
                    matchedInputs = matchedByNumAnywhere;
                }
            }
        }
        // === AKHIR LOGIKA BARU ===

        // Sort matchedInputs: prioritaskan file .psd
        matchedInputs.sort(function (a, b) {
            var aIsPsd = /\.psd$/i.test(a.name);
            var bIsPsd = /\.psd$/i.test(b.name);
            if (aIsPsd && !bIsPsd) return -1;
            if (!aIsPsd && bIsPsd) return 1;
            return a.name.toLowerCase() < b.name.toLowerCase() ? -1 : 1;
        });

        try {
            var doc = app.open(template);

            var smartXL = findSmartObject(doc, "XL");
            var smartS = findSmartObject(doc, "S");

            if (!smartXL) {
                failList.push(relPath(templateFolder, template) + " (Layer XL ga ada)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }

            if (matchedInputs.length == 2 && smartS) {
                // Replace XL & S normal
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");

                doc.activeLayer = smartS;
                replaceSmartContent(matchedInputs[1]);
                smartS.name = matchedInputs[1].displayName.replace(/\.[^\.]+$/, "");

                successList.push(relPath(templateFolder, template) + " (2 input: XL & S diganti)");
            } else if (matchedInputs.length == 1) {
                // Replace XL saja
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");

                successList.push(relPath(templateFolder, template) + " (1 input: cuma XL diganti)");
            } else {
                failList.push(relPath(templateFolder, template) + " (Input file ketemu " + matchedInputs.length + ", ga sesuai aturan)");
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

    // === Fungsi bantu ===
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

    function sortByNumberInFilename(a, b) {
        var aMatch = a.name.match(/(\d+)/);
        var bMatch = b.name.match(/(\d+)/);
        var aNum = aMatch ? parseInt(aMatch[1], 10) : 0;
        var bNum = bMatch ? parseInt(bMatch[1], 10) : 0;
        return aNum - bNum;
    }

    function findSmartObject(document, layerName) {
        for (var j = 0; j < document.artLayers.length; j++) {
            var layer = document.artLayers[j];
            if (layer.name == layerName && layer.kind == LayerKind.SMARTOBJECT) {
                return layer;
            }
        }
        for (var k = 0; k < document.layerSets.length; k++) {
            var found = searchInGroup(document.layerSets[k], layerName);
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
            var found = searchInGroup(group.layerSets[n], layerName);
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
