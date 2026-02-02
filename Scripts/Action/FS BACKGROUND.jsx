/**
 * DMA BG Panel — Pilih Folder Manual + Default + Style
 * - Pilih folder JPG secara manual
 * - Bisa set default folder
 * - Daftar file JPG, klik untuk place ke dokumen
 * - UI styled seperti replace foto.jsx
 */

var CONFIG_NAME = "DMA_BG_Panel_config.json";
var CONFIG_FILE = File(Folder.userData + "/" + CONFIG_NAME);
var ALLOWED_EXT = /\.(jpg|jpeg)$/i;

// ============== Config I/O ==============
function readConfig() {
    try {
        if (!CONFIG_FILE.exists) return {};
        CONFIG_FILE.open("r");
        var txt = CONFIG_FILE.read();
        CONFIG_FILE.close();
        if (typeof JSON === "undefined") {
            JSON = {};
            JSON.parse = function (s) { return eval('(' + s + ')'); };
        }
        return txt ? JSON.parse(txt) : {};
    } catch(e) {
        try{ CONFIG_FILE.close(); }catch(_){}
        alert("[Config] Gagal baca config:\n" + e);
        return {};
    }
}
function saveConfig(obj) {
    try {
        CONFIG_FILE.open("w");
        CONFIG_FILE.write(obj ? obj.toSource() : "{}");
        CONFIG_FILE.close();
    } catch(e) {
        try{ CONFIG_FILE.close(); }catch(_){}
        alert("[Config] Gagal simpan config:\n" + e);
    }
}

// ============== Util ==============
function ensureActiveDoc() {
    if (app.documents.length === 0) { alert("Tidak ada dokumen aktif."); return false; }
    return true;
}
function placeFileEmbedded(f) {
    try {
        var idPlc = charIDToTypeID("Plc ");
        var desc = new ActionDescriptor();
        desc.putPath(charIDToTypeID("null"), File(f));
        desc.putUnitDouble(charIDToTypeID("Wdth"), charIDToTypeID("#Prc"), 100);
        desc.putUnitDouble(charIDToTypeID("Hght"), charIDToTypeID("#Prc"), 100);
        desc.putUnitDouble(charIDToTypeID("Angl"), charIDToTypeID("#Ang"), 0);
        desc.putBoolean(stringIDToTypeID("linked"), false);
        executeAction(idPlc, desc, DialogModes.NO);
    } catch(e) { alert("[Place] " + e); }
}

// ============== UI ==============
function buildUI() {
    var w = new Window("dialog", "List Background", undefined, {resizeable:true});
    w.orientation = "column";
    w.alignChildren = ["fill","top"];
    w.margins = 20;
    w.spacing = 12;

    // === Style: background gelap ===
    try {
        w.graphics.backgroundColor = w.graphics.newBrush(
            w.graphics.BrushType.SOLID_COLOR, [0.18, 0.22, 0.28, 1]
        );
    } catch (_) {}

    var bar = w.add("group");
    bar.spacing = 6;
    var btnFolder = bar.add("button", undefined, "Pilih Folder...");
    var btnSetDefault = bar.add("button", undefined, "Set Default");
    var btnRefresh = bar.add("button", undefined, "Refresh");

    var list = w.add("listbox", undefined, [], {multiselect:false});
    list.preferredSize = [300, 320];

    // Path info pindah ke bawah, kecil & kuning
    var pathGroup = w.add("group");
    var infoText = pathGroup.add("statictext", undefined, "—", {truncate:"middle"});
    infoText.characters = 40; // Sesuaikan dengan lebar panel, misal 40
    infoText.justify = "left";
    try {
        infoText.graphics.foregroundColor = infoText.graphics.newPen(infoText.graphics.PenType.SOLID_COLOR, [1,1,0,1], 1); // kuning
    } catch(_) {}

    var currentFolder = null;

    function loadFolder(folder) {
        if (!folder || !folder.exists) {
            infoText.text = "(Folder tidak ditemukan)";
            list.removeAll();
            btnRefresh.enabled = false;
            btnSetDefault.enabled = false;
            return;
        }
        infoText.text = folder.fsName;
        currentFolder = folder;
        btnRefresh.enabled = true;
        btnSetDefault.enabled = true;
        var files = folder.getFiles();
        list.removeAll();
        var found = 0;
        for (var i=0; i<files.length; i++) {
            if (files[i] instanceof File && ALLOWED_EXT.test(files[i].name)) {
                var item = list.add("item", files[i].displayName);
                item.file = files[i];
                found++;
            }
        }
        if (found === 0) {
            list.add("item", "(Tidak ada file JPG)");
        }
    }

    btnFolder.onClick = function() {
        var f = Folder.selectDialog("Pilih folder JPG");
        if (f) loadFolder(f);
    };
    btnRefresh.onClick = function() {
        if (currentFolder) loadFolder(currentFolder);
    };
    btnSetDefault.onClick = function() {
        if (currentFolder && currentFolder.exists) {
            saveConfig({ defaultFolder: currentFolder.fsName });
            alert("Default folder disimpan!");
        }
    };
    list.onDoubleClick = function() {
        var sel = list.selection;
        if (!sel || !sel.file) return;
        if (!ensureActiveDoc()) return;
        placeFileEmbedded(sel.file.fsName);
        w.close(); // <-- Tambahkan baris ini agar panel tertutup otomatis
    };

    // Handler agar path selalu maksimal sesuai lebar panel
    w.onResize = function() {
        // Hitung characters berdasarkan lebar listbox
        var chars = Math.floor(list.size.width / 7); // 7px per karakter (kira-kira)
        if (chars < 10) chars = 10;
        infoText.characters = chars;
        infoText.size.width = list.size.width;
        pathGroup.size.width = list.size.width;
    };

    // Load default folder saat panel dibuka
    w.onShow = function() {
        var CFG = readConfig();
        var df = CFG.defaultFolder ? Folder(CFG.defaultFolder) : null;
        if (df && df.exists) loadFolder(df);
        w.onResize(); // <-- panggil agar path langsung menyesuaikan
    };

    $.global.DMA_BG_UI = w;
    return w;
}

// ================= Run =================
try {
    var ui = buildUI();
    ui.center();
    ui.show();
} catch(e) {
    alert("Gagal memulai panel:\n" + e + "\nLine: " + (e.line || "?"));
}
