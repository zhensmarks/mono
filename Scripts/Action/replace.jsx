// @target photoshop

// @target photoshop

// === Settings Persistence ===
function loadSettings() {
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
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
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
    try {
        settingsFile.open("w");
        settingsFile.write('{"x":' + x + ',"y":' + y + '}');
        settingsFile.close();
    } catch (e) { }
}



function main() {
    // === BMachine Integration (Pre-load context if available) ===
    var bmachineContext = null;
    var tempFile = new File(Folder.temp + "/bmachine_context.json");
    var defaultMasterPath = "";
    var defaultInputPath = "";

    if (tempFile.exists) {
        try {
            tempFile.open("r");
            var jsonContent = tempFile.read();
            tempFile.close();
            bmachineContext = eval("(" + jsonContent + ")");
        } catch (e) { }
    }

    // Determine defaults from Context only (No persistent path loading)
    if (bmachineContext) {
        if (bmachineContext.MasterTemplatePath && new Folder(bmachineContext.MasterTemplatePath).exists) {
            defaultMasterPath = bmachineContext.MasterTemplatePath;
        } else if (bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
            var path = bmachineContext.SourceFolders[0].OutputPath;
            if (path && new Folder(path).exists) defaultMasterPath = path;
        }

        if (bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0) {
            var path = bmachineContext.SourceFolders[0].SourcePath;
            if (path && new Folder(path).exists) defaultInputPath = path;
        }
    }

    // === UI CONFIG ===
    var settings = loadSettings();

    var w = new Window("dialog", "Smart Object Replacer");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 15;
    w.margins = 20;

    // --- Panel: Folder Master ---
    var pnlMaster = w.add("panel", undefined, "Folder Master (.psd)");
    pnlMaster.orientation = "column";
    pnlMaster.alignChildren = ["fill", "top"];
    pnlMaster.spacing = 8;
    pnlMaster.margins = 12;

    var grpMaster = pnlMaster.add("group");
    grpMaster.orientation = "row";
    grpMaster.alignChildren = ["fill", "center"];
    var txtMaster = grpMaster.add("edittext", undefined, defaultMasterPath);
    txtMaster.preferredSize.width = 310;

    var btnClearMaster = grpMaster.add("button", undefined, "X");
    btnClearMaster.size = [30, 25];
    btnClearMaster.helpTip = "Hapus & Fokus (Gunakan Win+V untuk Paste)";

    var btnBrowseMaster = grpMaster.add("button", undefined, "Browse...");
    btnBrowseMaster.preferredSize.width = 80;

    btnBrowseMaster.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Master");
        if (f) txtMaster.text = decodeURI(f.fullName);
    };

    btnClearMaster.onClick = function () {
        txtMaster.text = "";
        txtMaster.active = true;
    };

    // --- Panel: Folder Seleksi ---
    var pnlInput = w.add("panel", undefined, "Folder Seleksi (Input)");
    pnlInput.orientation = "column";
    pnlInput.alignChildren = ["fill", "top"];
    pnlInput.spacing = 8;
    pnlInput.margins = 12;

    var grpInput = pnlInput.add("group");
    grpInput.orientation = "row";
    grpInput.alignChildren = ["fill", "center"];
    var txtInput = grpInput.add("edittext", undefined, defaultInputPath);
    txtInput.preferredSize.width = 310;

    var btnClearInput = grpInput.add("button", undefined, "X");
    btnClearInput.size = [30, 25];
    btnClearInput.helpTip = "Hapus & Fokus (Gunakan Win+V untuk Paste)";

    var btnBrowseInput = grpInput.add("button", undefined, "Browse...");
    btnBrowseInput.preferredSize.width = 80;

    btnBrowseInput.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Seleksi");
        if (f) txtInput.text = decodeURI(f.fullName);
    };

    btnClearInput.onClick = function () {
        txtInput.text = "";
        txtInput.active = true;
    };

    // --- Buttons ---
    var grpBtn = w.add("group");
    grpBtn.alignment = "center";
    grpBtn.spacing = 10;
    var btnRun = grpBtn.add("button", undefined, "REPLACE", { name: "ok" });
    btnRun.preferredSize.width = 120;
    var btnRevisi = grpBtn.add("button", undefined, "REPLACE REVISI");
    btnRevisi.preferredSize.width = 130;
    var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });
    btnCancel.preferredSize.width = 80;

    // === EXECUTION LOGIC ===
    btnRun.onClick = function () {
        if (txtMaster.text == "") { alert("Folder Master belum diisi!"); return; }
        if (txtInput.text == "") { alert("Folder Seleksi belum diisi!"); return; }

        var resultMasterFolder = new Folder(txtMaster.text);
        var resultInputFolder = new Folder(txtInput.text);

        if (!resultMasterFolder.exists) { alert("Folder Master tidak ditemukan!"); return; }
        if (!resultInputFolder.exists) { alert("Folder Seleksi tidak ditemukan!"); return; }

        // Save Window Position only
        saveSettings(w.location.x, w.location.y);

        w.close();
        runReplacementLogic(resultMasterFolder, resultInputFolder);
    };

    // === REPLACE REVISI LOGIC ===
    btnRevisi.onClick = function () {
        if (txtMaster.text == "") { alert("Folder Master belum diisi!"); return; }
        if (txtInput.text == "") { alert("Folder Seleksi belum diisi!"); return; }

        var resultMasterFolder = new Folder(txtMaster.text);
        var resultInputFolder = new Folder(txtInput.text);

        if (!resultMasterFolder.exists) { alert("Folder Master tidak ditemukan!"); return; }
        if (!resultInputFolder.exists) { alert("Folder Seleksi tidak ditemukan!"); return; }

        // Save Window Position only
        saveSettings(w.location.x, w.location.y);

        w.close();
        runRevisiLogic(resultMasterFolder, resultInputFolder);
    };

    btnCancel.onClick = function () {
        w.close();
    };

    // Restore window position
    if (settings.x > 0 && settings.y > 0) {
        w.location = [settings.x, settings.y];
    } else {
        w.center();
    }

    w.show();

}

// Global invocation
main();

// ==========================================
// Core Logic (Helper Functions)
// ==========================================
function runReplacementLogic(templateFolder, inputFolder) {

    // --- Scan Files ---
    var templateFiles = scanFolderForFiles(templateFolder, /\.(psd|psb)$/i);
    var allInputFiles = scanFolderForFiles(inputFolder, /\.(png|psd|jpe?g)$/i);

    // --- Determine Mode (PNG/PSD vs JPG) ---
    var hasPngPsd = false;
    for (var k = 0; k < allInputFiles.length; k++) {
        if (allInputFiles[k].name.match(/\.(png|psd)$/i)) {
            hasPngPsd = true;
            break;
        }
    }

    var inputFiles = [];

    if (hasPngPsd) {
        for (var k = 0; k < allInputFiles.length; k++) {
            if (allInputFiles[k].name.match(/\.(png|psd)$/i)) inputFiles.push(allInputFiles[k]);
        }
    } else {
        inputFiles = allInputFiles;
    }

    templateFiles.sort(sortByNumberInFilename);
    inputFiles.sort(sortByNumberInFilename);

    var templateCount = templateFiles.length;
    var inputCount = inputFiles.length;

    if (templateCount == 0 || inputCount == 0) {
        alert("Tidak ada file yang cocok.\nMaster: " + templateCount + "\nInput: " + inputCount);
        return;
    }

    // --- Processing Loop ---
    var successList = [];
    var failList = [];

    function reportProgress(current, total, filename) {
        var f = new File(Folder.temp + "/bmachine_progress.json");
        f.open("w");
        f.write('{"current": ' + current + ', "total": ' + total + ', "file": "' + filename + '", "status": "processing"}');
        f.close();
    }

    for (var i = 0; i < templateFiles.length; i++) {
        var template = templateFiles[i];
        reportProgress(i + 1, templateCount, template.name);

        // --- Matching Logic ---
        var matchedInputs = [];
        var templateRelDir = decodeURI(template.parent.fullName).replace(decodeURI(templateFolder.fullName), "");
        if (templateRelDir.indexOf("/") == 0) templateRelDir = templateRelDir.substring(1);

        // 1. By Name
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

                    if (inputRelDir == templateRelDir) matchedByNameInSubfolder.push(input);
                    else matchedByNameAnywhere.push(input);
                }
            }
            if (matchedByNameInSubfolder.length > 0) matchedInputs = matchedByNameInSubfolder;
            else if (matchedByNameAnywhere.length > 0) matchedInputs = matchedByNameAnywhere;
        }

        // 2. By Number (if Name failed)
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

                        if (inputRelDir == templateRelDir) matchedByNumInSubfolder.push(input);
                        else matchedByNumAnywhere.push(input);
                    }
                }
                if (matchedByNumInSubfolder.length > 0) matchedInputs = matchedByNumInSubfolder;
                else if (matchedByNumAnywhere.length > 0) matchedInputs = matchedByNumAnywhere;
            }
        }

        // Sort matches
        matchedInputs.sort(function (a, b) {
            var aIsPng = /\.png$/i.test(a.name);
            var bIsPng = /\.png$/i.test(b.name);
            var aIsPsd = /\.psd$/i.test(a.name);
            var bIsPsd = /\.psd$/i.test(b.name);
            if (aIsPng && !bIsPng) return -1;
            if (!aIsPng && bIsPng) return 1;
            if (aIsPsd && !bIsPsd) return -1;
            if (!aIsPsd && bIsPsd) return 1;
            return a.name.toLowerCase() < b.name.toLowerCase() ? -1 : 1;
        });

        try {
            var doc = app.open(template);
            var smartXL = findSmartObject(doc, "XL");
            var smartS = findSmartObject(doc, "S");

            if (!smartXL) {
                failList.push(relPath(templateFolder, template) + " (Layer XL missing)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }

            if (smartS && matchedInputs.length >= 2) {
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");
                doc.activeLayer.rasterize(RasterizeType.ENTIRELAYER);

                doc.activeLayer = smartS;
                replaceSmartContent(matchedInputs[1]);
                smartS.name = matchedInputs[1].displayName.replace(/\.[^\.]+$/, "");
                doc.activeLayer.rasterize(RasterizeType.ENTIRELAYER);

                successList.push(relPath(templateFolder, template));
            } else if (matchedInputs.length >= 1) {
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");
                doc.activeLayer.rasterize(RasterizeType.ENTIRELAYER);
                successList.push(relPath(templateFolder, template));
            } else {
                failList.push(relPath(templateFolder, template) + " (No input match)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }
        } catch (e) {
            failList.push(relPath(templateFolder, template) + " (Error: " + e.message + ")");
        }
    }

    // --- Summary ---
    var report = [];
    report.push("Master: " + (templateFiles.length > 0 ? templateFiles[0].name : "-"));
    report.push("Total: " + templateFiles.length);
    report.push("Processed: " + successList.length);
    report.push("Failed: " + failList.length);

    if (failList.length > 0) {
        report.push("\nFAIL DETAIL:");
        report = report.concat(failList);
    }

    // Send Result to BMachine
    var f = new File(Folder.temp + "/bmachine_result.json");
    f.open("w");
    f.encoding = "UTF-8";
    var escaped = [];
    for (var i = 0; i < report.length; i++) {
        escaped.push('"' + report[i].replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"');
    }
    f.write('{"type":"result","title":"Replacer Summary","lines":[' + escaped.join(',') + ']}');
    f.close();

    alert("Selesai!\nBerhasil: " + successList.length + "\nGagal: " + failList.length);
}

// === Helpers ===
function scanFolderForFiles(folder, regex) {
    var files = [];
    var items = folder.getFiles();
    for (var i = 0; i < items.length; i++) {
        var item = items[i];
        if (item instanceof File && item.name.match(regex)) files.push(item);
        else if (item instanceof Folder) files = files.concat(scanFolderForFiles(item, regex));
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
        if (layer.name == layerName && layer.kind == LayerKind.SMARTOBJECT) return layer;
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
        if (l.name == layerName && l.kind == LayerKind.SMARTOBJECT) return l;
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

// ==========================================
// REPLACE REVISI Logic
// Match Smart Object name with input files
// ==========================================
function runRevisiLogic(masterFolder, inputFolder) {
    // Scan for PSD/PSB files in master folder
    var masterFiles = scanFolderForFiles(masterFolder, /\.(psd|psb)$/i);
    masterFiles.sort(sortByNumberInFilename);

    // Scan for replacement files in input folder
    var inputFiles = scanFolderForFiles(inputFolder, /\.(png|psd|jpe?g)$/i);

    if (masterFiles.length == 0) {
        alert("Tidak ada file PSD/PSB di Folder Master!");
        return;
    }

    if (inputFiles.length == 0) {
        alert("Tidak ada file gambar di Folder Seleksi!");
        return;
    }

    // Build a lookup map: filename (without extension) -> file object
    // Priority: PNG > PSD > JPG
    var inputMap = {};

    // First pass: add all files
    for (var i = 0; i < inputFiles.length; i++) {
        var file = inputFiles[i];
        var baseName = file.displayName.replace(/\.[^\.]+$/, "").toLowerCase();

        if (!inputMap[baseName]) {
            inputMap[baseName] = file;
        } else {
            // Check priority: PNG > PSD > JPG
            var existingExt = inputMap[baseName].name.match(/\.([^\.]+)$/i)[1].toLowerCase();
            var newExt = file.name.match(/\.([^\.]+)$/i)[1].toLowerCase();

            var priority = { "png": 3, "psd": 2, "jpg": 1, "jpeg": 1 };
            if ((priority[newExt] || 0) > (priority[existingExt] || 0)) {
                inputMap[baseName] = file;
            }
        }
    }

    var successList = [];
    var failList = [];
    var totalSmartObjects = 0;
    var replacedCount = 0;

    // Process each master file
    for (var m = 0; m < masterFiles.length; m++) {
        var masterFile = masterFiles[m];

        try {
            var doc = app.open(masterFile);

            // Find all smart objects in the document
            var smartObjects = findAllSmartObjects(doc);
            totalSmartObjects += smartObjects.length;

            if (smartObjects.length == 0) {
                failList.push(relPath(masterFolder, masterFile) + " (No Smart Object found)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }

            var fileReplaced = false;

            // Try to replace each smart object
            for (var s = 0; s < smartObjects.length; s++) {
                var smartObj = smartObjects[s];
                var smartName = smartObj.name.toLowerCase();

                // Look for matching file in inputMap
                if (inputMap[smartName]) {
                    try {
                        doc.activeLayer = smartObj;
                        replaceSmartContent(inputMap[smartName]);
                        replacedCount++;
                        fileReplaced = true;
                    } catch (e) {
                        // Skip if replacement fails
                    }
                }
            }

            if (fileReplaced) {
                successList.push(relPath(masterFolder, masterFile));
            } else {
                failList.push(relPath(masterFolder, masterFile) + " (No matching input file)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }

        } catch (e) {
            failList.push(relPath(masterFolder, masterFile) + " (Error: " + e.message + ")");
        }
    }

    // Summary
    var report = [];
    report.push("=== REPLACE REVISI ===");
    report.push("Master Files: " + masterFiles.length);
    report.push("Input Files: " + inputFiles.length);
    report.push("Smart Objects Found: " + totalSmartObjects);
    report.push("Replaced: " + replacedCount);
    report.push("Success: " + successList.length);
    report.push("Failed: " + failList.length);

    if (failList.length > 0) {
        report.push("\nFAIL DETAIL:");
        report = report.concat(failList);
    }

    alert("Selesai!\n\nSmart Object Replaced: " + replacedCount + "\nFile Berhasil: " + successList.length + "\nFile Gagal: " + failList.length);
}

// Find all Smart Objects in document (including nested in groups)
function findAllSmartObjects(document) {
    var result = [];

    // Search in artLayers
    for (var i = 0; i < document.artLayers.length; i++) {
        var layer = document.artLayers[i];
        if (layer.kind == LayerKind.SMARTOBJECT) {
            result.push(layer);
        }
    }

    // Search in layer sets (groups)
    for (var j = 0; j < document.layerSets.length; j++) {
        result = result.concat(findSmartObjectsInGroup(document.layerSets[j]));
    }

    return result;
}

function findSmartObjectsInGroup(group) {
    var result = [];

    for (var i = 0; i < group.artLayers.length; i++) {
        var layer = group.artLayers[i];
        if (layer.kind == LayerKind.SMARTOBJECT) {
            result.push(layer);
        }
    }

    for (var j = 0; j < group.layerSets.length; j++) {
        result = result.concat(findSmartObjectsInGroup(group.layerSets[j]));
    }

    return result;
}
