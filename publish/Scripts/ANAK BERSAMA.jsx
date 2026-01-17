// === UI Pilihan Gaya ===
var gayaDialog = new Window("dialog", "Pilih Gaya");
gayaDialog.orientation = "column";
gayaDialog.alignChildren = "center";
gayaDialog.add("statictext", undefined, "Pilih Gaya:");
var btn1 = gayaDialog.add("button", undefined, "Gaya 1");
var btn2 = gayaDialog.add("button", undefined, "Gaya 2");

var gayaDipilih = "1";
btn1.onClick = function () { gayaDipilih = "1"; gayaDialog.close(); };
btn2.onClick = function () { gayaDipilih = "2"; gayaDialog.close(); };
gayaDialog.show();

// === Pilih folder utama ===
var masterFolder = Folder.selectDialog("Pilih seleksiana");
if (!masterFolder) {
    alert("Ga jadi");
    exit();
}

// === Ambil File PNG/PSD sesuai gaya dari folder utama (termasuk subfolder) ===
var imageFiles = getAllImageFilesRecursive(masterFolder, gayaDipilih);
var totalFiles = imageFiles.length;

// === Siapkan Group "ANAK" di PSD aktif ===
var doc = app.activeDocument;
var groupAnak;
try {
    groupAnak = doc.layerSets.getByName("ANAK");
} catch (e) {
    groupAnak = doc.layerSets.add();
    groupAnak.name = "ANAK";
}

// === UI Progress Bar ===
var progressWin = new Window("palette", "Sabar, lagi proses...", undefined);
progressWin.orientation = "column";
progressWin.alignChildren = "fill";
var statusText = progressWin.add("statictext", undefined, "Gaskeun...");
var progressBar = progressWin.add("progressbar", undefined, 0, totalFiles);
progressBar.preferredSize = [300, 20];
progressWin.center();
progressWin.show();

// === Proses Batch ===
for (var i = 0; i < imageFiles.length; i++) {
    var file = imageFiles[i];
    var persen = Math.round(((i + 1) / totalFiles) * 100);
    statusText.text = "Proses cuy: " + persen + "%";
    progressBar.value = i + 1;
    app.refresh();
    processFile(file, groupAnak);
}

alert(
    "Total: " + totalFiles + " file\n" +
    "Beres yeuh, geura susun"
);

// === Fungsi ambil file PNG/PSD sesuai gaya dari folder & subfolder ===
function getAllImageFilesRecursive(folder, gaya) {
    var files = [];
    var items = folder.getFiles();
    for (var i = 0; i < items.length; i++) {
        var item = items[i];
        if (item instanceof Folder) {
            files = files.concat(getAllImageFilesRecursive(item, gaya));
        } else if (
            item instanceof File &&
            (
                (item.name.match(/\.png$/i) && item.name.indexOf("(" + gaya + ").png") !== -1) ||
                (item.name.match(/\.psd$/i) && item.name.indexOf("(" + gaya + ").psd") !== -1)
            )
        ) {
            files.push(item);
        }
    }
    return files;
}

// === Fungsi proses file ke grup ANAK ===
function processFile(file, groupAnak) {
    var targetDoc = app.activeDocument;
    var tempDoc = app.open(file);

    // Pastikan dokumen sumber punya layer
    if (tempDoc.layers.length > 0) {
        var srcLayer = tempDoc.activeLayer;
        app.activeDocument = tempDoc;
        srcLayer.duplicate(targetDoc, ElementPlacement.PLACEATBEGINNING);
        tempDoc.close(SaveOptions.DONOTSAVECHANGES);
        app.activeDocument = targetDoc;
        var newLayer = targetDoc.activeLayer;
        newLayer.move(groupAnak, ElementPlacement.INSIDE);
        newLayer.name = decodeURI(file.name); // Nama layer sesuai nama file asli

        // Jadikan layer sebagai Smart Object
        app.activeDocument.activeLayer = newLayer;
        var idnewPlacedLayer = stringIDToTypeID("newPlacedLayer");
        executeAction(idnewPlacedLayer, undefined, DialogModes.NO);
    } else {
        tempDoc.close(SaveOptions.DONOTSAVECHANGES);
    }
}
