// Photoshop JSX Script
// Buka banyak file PSD dari folder (natural sort A-Z + angka)
// Update: UI Textbox, mengingat path, dan mengingat posisi kedua dialog.

#target photoshop

// --- Fungsi Persistence (Menyimpan & Memuat Semua Pengaturan) ---
function loadSettings() {
    var settingsFile = new File(Folder.userData + "/open_psd_final_settings.json");
    if (settingsFile.exists) {
        try {
            settingsFile.open("r");
            var content = settingsFile.read();
            settingsFile.close();
            return eval("(" + content + ")");
        } catch (e) { /* Abaikan error, gunakan default */ }
    }
    // Pengaturan default jika file tidak ada atau rusak
    return {
        lastPath: "",
        main_x: -1, main_y: -1,
        list_x: -1, list_y: -1
    };
}

function saveSettings(settingsObj) {
    var settingsFile = new File(Folder.userData + "/open_psd_final_settings.json");
    try {
        var settingsStr = '{';
        settingsStr += '"lastPath":"' + (settingsObj.lastPath || "").replace(/\\/g, '\\\\') + '",';
        settingsStr += '"main_x":' + (settingsObj.main_x || -1) + ',';
        settingsStr += '"main_y":' + (settingsObj.main_y || -1) + ',';
        settingsStr += '"list_x":' + (settingsObj.list_x || -1) + ',';
        settingsStr += '"list_y":' + (settingsObj.list_y || -1);
        settingsStr += '}';

        settingsFile.open("w");
        settingsFile.write(settingsStr);
        settingsFile.close();
    } catch (e) { /* Gagal menyimpan tidak menghentikan script */ }
}


function getPSDfiles(folder) {
    var psdFiles = [];
    var files = folder.getFiles();
    for (var i = 0; i < files.length; i++) {
        var f = files[i];
        if (f instanceof Folder) {
            psdFiles = psdFiles.concat(getPSDfiles(f));
        } else if (f instanceof File && f.name.toLowerCase().match(/\.psd$/)) {
            psdFiles.push(f);
        }
    }
    return psdFiles;
}

// --- Fungsi natural sort ---
// --- Fungsi natural sort (Modified for Parent/Name) ---
function naturalSort(a, b) {
    var ax = [], bx = [];

    // Construct comparison strings: ParentName/FileName
    var nameA = decodeURI(a.parent.name) + "/" + decodeURI(a.name);
    var nameB = decodeURI(b.parent.name) + "/" + decodeURI(b.name);

    nameA.replace(/(\d+)|(\D+)/g, function (_, $1, $2) {
        ax.push([$1 || Infinity, $2 || ""]);
    });
    nameB.replace(/(\d+)|(\D+)/g, function (_, $1, $2) {
        bx.push([$1 || Infinity, $2 || ""]);
    });

    while (ax.length && bx.length) {
        var an = ax.shift();
        var bn = bx.shift();
        var nn = (an[0] - bn[0]) || an[1].localeCompare(bn[1]);
        if (nn) return nn;
    }

    return ax.length - bx.length;
}

function main() {
    var settings = loadSettings();

    // === DIALOG UTAMA (Pilih Folder dengan Textbox) ===
    var dlg_main = new Window("dialog", "Pilih Folder Master");
    dlg_main.orientation = "column";
    dlg_main.alignChildren = ["fill", "top"];
    dlg_main.spacing = 15;
    dlg_main.margins = 20;

    var pnlFolder = dlg_main.add("panel", undefined, "Folder Master (.psd)");
    pnlFolder.orientation = "column";
    pnlFolder.alignChildren = ["fill", "top"];
    pnlFolder.spacing = 8;
    pnlFolder.margins = 12;

    var grpFolder = pnlFolder.add("group");
    grpFolder.orientation = "row";
    grpFolder.alignChildren = ["fill", "center"];
    var txtFolder = grpFolder.add("edittext", undefined, decodeURI(settings.lastPath));
    txtFolder.preferredSize.width = 310;

    var btnClear = grpFolder.add("button", undefined, "X");
    btnClear.size = [30, 25];
    btnClear.helpTip = "Hapus path";

    var btnBrowse = grpFolder.add("button", undefined, "Browse...");
    btnBrowse.preferredSize.width = 80;

    btnBrowse.onClick = function () {
        var f = Folder(txtFolder.text || settings.lastPath).selectDlg("Pilih Folder Master");
        if (f) txtFolder.text = decodeURI(f.fullName);
    };

    btnClear.onClick = function () {
        txtFolder.text = "";
        txtFolder.active = true;
    };

    var btnGroupMain = dlg_main.add("group");
    btnGroupMain.alignment = "center";
    btnGroupMain.add("button", undefined, "Lanjutkan", { name: "ok" });
    btnGroupMain.add("button", undefined, "Batal", { name: "cancel" });

    // Kembalikan posisi dialog utama
    if (settings.main_x > 0 && settings.main_y > 0) {
        dlg_main.location = [settings.main_x, settings.main_y];
    } else {
        dlg_main.center();
    }

    if (dlg_main.show() != 1) return;

    // Simpan path & posisi dialog utama
    // Sanitize path (remove quotes and extra spaces)
    var rawPath = txtFolder.text;
    if (rawPath) {
        rawPath = rawPath.replace(/^["']+|["']+$/g, "").replace(/^\s+|\s+$/g, "");
    }

    settings.lastPath = rawPath;
    settings.main_x = dlg_main.location.x;
    settings.main_y = dlg_main.location.y;
    saveSettings(settings);

    var folder = new Folder(settings.lastPath);
    if (!folder.exists) {
        alert("Folder tidak ditemukan:\n" + settings.lastPath + "\n(Pastikan path benar dan folder ada)");
        return;
    }

    var psdFiles = getPSDfiles(folder);
    if (psdFiles.length === 0) {
        alert("Tidak ada file PSD ditemukan di folder tersebut.");
        return;
    }

    psdFiles.sort(naturalSort);

    // --- DIALOG KEDUA (Pilih File dari Daftar) ---
    var dlg_list = new Window("dialog", "Pilih File PSD untuk Dibuka");
    dlg_list.orientation = "column";
    dlg_list.alignChildren = ["fill", "fill"]; // Allow expanding

    // Kembalikan posisi dialog daftar
    if (settings.list_x > 0 && settings.list_y > 0) {
        dlg_list.location = [settings.list_x, settings.list_y];
    } else {
        dlg_list.center();
    }

    var grpHeader = dlg_list.add("group");
    grpHeader.orientation = "row";
    grpHeader.alignChildren = ["left", "center"];
    grpHeader.add("statictext", undefined, "Daftar File PSD (" + psdFiles.length + " items):");

    // Tombol Pilih Semua (Select All) - Bertindak sebagai Check All
    var btnSelectAll = grpHeader.add("button", undefined, "Pilih Semua");
    btnSelectAll.size = [100, 25];
    btnSelectAll.onClick = function () {
        // Toggle selection based on first item
        var newState = (list.items.length > 0 && !list.items[0].selected);
        for (var i = 0; i < list.items.length; i++) {
            list.items[i].selected = newState;
        }
        // Update text logic (optional)
        btnSelectAll.text = newState ? "Hapus Pilihan" : "Pilih Semua";
    };

    var list = dlg_list.add("listbox", undefined, [], { multiselect: true });
    list.preferredSize.width = 400;
    list.preferredSize.height = 300;

    for (var i = 0; i < psdFiles.length; i++) {
        var displayName = decodeURI(psdFiles[i].parent.name) + "/" + decodeURI(psdFiles[i].name);
        var item = list.add("item", displayName);
        item.fileRef = psdFiles[i];
    }

    var btnGroupList = dlg_list.add("group");
    btnGroupList.alignment = "center";

    // Tombol Buka (Hanya yang terceklis/terpilih)
    var btnOpen = btnGroupList.add("button", undefined, "Buka (Selected)", { name: "ok" });

    // Tombol Buka Semua (Open All) - Request User
    var btnOpenAll = btnGroupList.add("button", undefined, "Buka Semua");

    var btnCancel = btnGroupList.add("button", undefined, "Batal", { name: "cancel" });

    btnCancel.onClick = function () {
        dlg_list.close(2);
    };

    // Logic Tombol Buka Semua
    btnOpenAll.onClick = function () {
        dlg_list.close(3); // Return code 3 for 'Open All' (2 is for Cancel)
    };

    // Logic Tombol Buka (Selected) - Default 'ok' button behavior
    btnOpen.onClick = function () {
        dlg_list.close(1); // Return code 1 for 'Open Selected'
    };

    var listResult = dlg_list.show();

    // Simpan posisi dialog daftar
    settings.list_x = dlg_list.location.x;
    settings.list_y = dlg_list.location.y;
    saveSettings(settings);

    // Handle Results
    if (listResult === 3) {
        // OPEN ALL
        if (psdFiles.length === 0) return;
        for (var i = 0; i < psdFiles.length; i++) {
            app.open(psdFiles[i]);
        }
        alert("Selesai membuka SEMUA (" + psdFiles.length + ") file PSD.");

    } else if (listResult === 1) {
        // OPEN SELECTED
        var selected = list.selection;
        if (!selected || selected.length === 0) {
            // Coba cek user mungkin salah paham, jika tidak ada selected tapi klik buka, 
            // kita bisa tanya atau default batal. Script ini akan batal.
            // Tapi user minta "logika jika ada ceklist ... buka yang terceklis".
            // Jika tidak ada yang terceklis, kita anggap user batal atau lupa.
            alert("Tidak ada file yang dipilih (dicentang).");
            return;
        }

        for (var i = 0; i < selected.length; i++) {
            app.open(selected[i].fileRef);
        }
        alert("Selesai membuka " + selected.length + " file PSD yang dipilih.");
    }
    // Else Cancel (0)
}

main();
