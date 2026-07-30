// @target photoshop

/*
    batch_save_master.jsx
    Fitur: Proses batch PSD/PSB dari daftar folder/file untuk di-Save & Export ke JPG (Quality 12) lalu tutup.
*/

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/batch_save_master_settings.json");
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
    var settingsFile = new File(Folder.userData + "/batch_save_master_settings.json");
    try {
        settingsFile.open("w");
        settingsFile.write('{"x":' + x + ',"y":' + y + '}');
        settingsFile.close();
    } catch (e) { }
}

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

function saveJPG(doc, filePath, quality) {
    var jpgOptions = new JPEGSaveOptions();
    jpgOptions.quality = quality;
    jpgOptions.embedColorProfile = true;
    jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
    jpgOptions.scans = 3;
    doc.saveAs(new File(filePath), jpgOptions, true, Extension.LOWERCASE);
}

function main() {
    var settings = loadSettings();

    // === UI DIALOG ===
    var w = new Window("dialog", "Batch Save Master JPG");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 15;
    w.margins = 20;

    // --- PANEL: CONFIG ---
    var pnlConfig = w.add("panel", undefined, "Konfigurasi Path (Folder atau File)");
    pnlConfig.orientation = "column";
    pnlConfig.alignChildren = ["fill", "top"];
    pnlConfig.spacing = 10;
    pnlConfig.margins = 15;

    // Row 1: Input
    var grpInput = pnlConfig.add("group");
    grpInput.orientation = "row";
    grpInput.alignChildren = ["fill", "center"];

    var lblInput = grpInput.add("statictext", undefined, "Path:");
    lblInput.preferredSize.width = 40;

    var txtInput = grpInput.add("edittext", undefined, "");
    txtInput.preferredSize.width = 230;

    var btnClearInput = grpInput.add("button", undefined, "X");
    btnClearInput.size = [25, 25];
    btnClearInput.helpTip = "Hapus teks";

    var btnBrowseFolder = grpInput.add("button", undefined, "Folder...");
    btnBrowseFolder.preferredSize.width = 70;

    var btnBrowseFile = grpInput.add("button", undefined, "File...");
    btnBrowseFile.preferredSize.width = 60;

    // Row 2: Add button
    var grpAdd = pnlConfig.add("group");
    grpAdd.orientation = "row";
    grpAdd.alignChildren = ["right", "center"];

    var btnAddQueue = grpAdd.add("button", undefined, "+");
    btnAddQueue.size = [40, 25];
    btnAddQueue.helpTip = "Tambahkan ke antrian";

    // --- PANEL: QUEUE LIST ---
    var grpQueue = w.add("group");
    grpQueue.orientation = "column";
    grpQueue.alignChildren = ["fill", "top"];
    grpQueue.visible = false;
    grpQueue.spacing = 5;

    var lblQueue = grpQueue.add("statictext", undefined, "Daftar Antrian (Queue):");
    var listQueue = grpQueue.add("listbox", undefined, [], { multiselect: true });
    listQueue.preferredSize.height = 150;
    listQueue.preferredSize.width = 450;

    var grpQueueControl = grpQueue.add("group");
    grpQueueControl.orientation = "row";
    grpQueueControl.alignChildren = ["left", "center"];

    var btnClearQueue = grpQueueControl.add("button", undefined, "Hapus Terpilih");
    btnClearQueue.size = [120, 24];
    btnClearQueue.enabled = false;

    // --- EVENTS ---
    btnBrowseFolder.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Target");
        if (f) txtInput.text = decodeURI(f.fullName);
    };

    btnBrowseFile.onClick = function () {
        var f = File.openDialog("Pilih File PSD/PSB", "Photoshop Document:*.psd;*.psb");
        if (f) txtInput.text = decodeURI(f.fullName);
    };

    btnClearInput.onClick = function () {
        txtInput.text = "";
        txtInput.active = true;
    };

    var queueData = [];

    btnAddQueue.onClick = function () {
        var pathStr = txtInput.text;
        if (pathStr === "") {
            alert("Pilih folder atau file dulu!");
            return;
        }

        queueData.push(pathStr);
        listQueue.add("item", pathStr);

        grpQueue.visible = true;
        w.layout.layout(true);

        txtInput.text = "";
    };

    listQueue.onChange = function () {
        btnClearQueue.enabled = (listQueue.selection != null);
    };

    btnClearQueue.onClick = function () {
        if (!listQueue.selection) return;
        var limits = listQueue.selection;
        var indices = [];
        for (var i = 0; i < limits.length; i++) indices.push(limits[i].index);
        indices.sort(function (a, b) { return b - a }); // Descending

        for (var i = 0; i < indices.length; i++) {
            var idx = indices[i];
            listQueue.remove(idx);
            queueData.splice(idx, 1);
        }

        if (listQueue.items.length == 0) {
            grpQueue.visible = false;
            w.layout.layout(true);
        }
    };

    // --- BOTTOM BUTTONS ---
    var grpBtn = w.add("group");
    grpBtn.alignment = "center";
    grpBtn.spacing = 10;

    var btnRun = grpBtn.add("button", undefined, "RUN", { name: "ok" });
    btnRun.preferredSize.width = 120;
    var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });
    btnCancel.preferredSize.width = 80;

    btnRun.onClick = function () {
        w.close(1);
    };

    btnCancel.onClick = function () {
        w.close(0);
    };

    // Restore Location
    if (settings.x > 0 && settings.y > 0) {
        w.location = [settings.x, settings.y];
    } else {
        w.center();
    }

    var result = w.show();
    if (result != 1) return; // Batal / Cancel

    saveSettings(w.location.x, w.location.y);

    // --- PROSES ANTRIAN ---
    var pathsToProcess = [];
    for (var i = 0; i < queueData.length; i++) {
        pathsToProcess.push(queueData[i]);
    }
    if (txtInput.text !== "") {
        pathsToProcess.push(txtInput.text);
    }

    if (pathsToProcess.length === 0) {
        alert("Tidak ada target file atau folder yang dipilih!");
        return;
    }

    var filesToProcess = [];
    for (var i = 0; i < pathsToProcess.length; i++) {
        var pathStr = pathsToProcess[i];
        var folderObj = new Folder(pathStr);
        var fileObj = new File(pathStr);

        if (folderObj.exists) {
            var found = scanFolderForFiles(folderObj, /\.(psd|psb)$/i);
            filesToProcess = filesToProcess.concat(found);
        } else if (fileObj.exists) {
            if (fileObj.name.match(/\.(psd|psb)$/i)) {
                filesToProcess.push(fileObj);
            }
        }
    }

    // Deduplicate
    var uniqueFiles = [];
    var seen = {};
    for (var i = 0; i < filesToProcess.length; i++) {
        var f = filesToProcess[i];
        var key = f.fullName;
        if (!seen[key]) {
            seen[key] = true;
            uniqueFiles.push(f);
        }
    }
    filesToProcess = uniqueFiles;

    if (filesToProcess.length === 0) {
        alert("Tidak ditemukan file PSD/PSB untuk diproses.");
        return;
    }

    var successList = [];
    var failList = [];

    for (var j = 0; j < filesToProcess.length; j++) {
        var fileObj = filesToProcess[j];
        var doc = null;
        try {
            // 1. Open Document
            doc = app.open(fileObj);
            var baseName = doc.name.replace(/\.[^\.]+$/, "");
            var docPath = doc.path ? doc.path : fileObj.parent;

            // 2. Save original (PSD/PSB)
            doc.save();

            // 3. Duplicate & Flatten
            var dupDoc = doc.duplicate(baseName + "_temp");
            app.activeDocument = dupDoc;

            dupDoc.artLayers.add();
            dupDoc.flatten();

            // 4. Action 'anti ramijud'
            try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }

            // 5. Save JPG (Quality 12)
            saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", 12);

            // 6. Close duplicate
            dupDoc.close(SaveOptions.DONOTSAVECHANGES);

            // 7. Close original
            doc.close(SaveOptions.DONOTSAVECHANGES);

            successList.push(fileObj.name);
        } catch (e) {
            failList.push(fileObj.name + " (Error: " + e.message + ")");
            // Jangan close PSD jika gagal, biarkan tetap terbuka agar user bisa memeriksa
        }
    }

    // --- LAPORAN FINAL ---
    var msg = "Proses selesai.\n\n";
    msg += "Sukses: " + successList.length + "\n";
    if (successList.length > 0) {
        msg += successList.join("\n") + "\n\n";
    }
    msg += "Gagal: " + failList.length + "\n";
    if (failList.length > 0) {
        msg += failList.join("\n");
    }

    showScrollableAlert("Laporan Batch Save Master JPG", msg);
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

// Jalankan program utama
main();
