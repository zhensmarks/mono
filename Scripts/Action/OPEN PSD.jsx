// Photoshop JSX Script
// Buka banyak file PSD dari folder (natural sort A-Z + angka)
// Update: 2-Column Layout, Filter Revisi, Sort Options, Restore "Buka Semua", Fix Cancel Logic

#target photoshop

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/open_psd_final_settings.json");
    if (settingsFile.exists) {
        try {
            settingsFile.open("r");
            return eval("(" + settingsFile.read() + ")");
        } catch (e) { }
    }
    return { lastPath: "", main_x: -1, main_y: -1, list_x: -1, list_y: -1, sort_asc: false };
}

function saveSettings(settingsObj) {
    var settingsFile = new File(Folder.userData + "/open_psd_final_settings.json");
    try {
        var str = '{';
        str += '"lastPath":"' + (settingsObj.lastPath || "").replace(/\\/g, '\\\\') + '",';
        str += '"main_x":' + (settingsObj.main_x || -1) + ',';
        str += '"main_y":' + (settingsObj.main_y || -1) + ',';
        str += '"list_x":' + (settingsObj.list_x || -1) + ',';
        str += '"list_y":' + (settingsObj.list_y || -1) + ',';
        str += '"sort_asc":' + (settingsObj.sort_asc || false);
        str += '}';
        settingsFile.open("w");
        settingsFile.write(str);
        settingsFile.close();
    } catch (e) { }
}

function getPSDfiles(folder) {
    var psdFiles = [];
    var files = folder.getFiles();
    for (var i = 0; i < files.length; i++) {
        var f = files[i];
        if (f instanceof Folder) {
            psdFiles = psdFiles.concat(getPSDfiles(f));
        } else if (f instanceof File && f.name.toLowerCase().match(/\.(psd|psb)$/)) {
            psdFiles.push(f);
        }
    }
    return psdFiles;
}

function naturalSort(a, b) {
    var ax = [], bx = [];
    var nameA = decodeURI(a.parent.name) + "/" + decodeURI(a.name);
    var nameB = decodeURI(b.parent.name) + "/" + decodeURI(b.name);
    nameA.replace(/(\d+)|(\D+)/g, function (_, $1, $2) { ax.push([$1 || Infinity, $2 || ""]); });
    nameB.replace(/(\d+)|(\D+)/g, function (_, $1, $2) { bx.push([$1 || Infinity, $2 || ""]); });
    while (ax.length && bx.length) {
        var an = ax.shift(), bn = bx.shift();
        var nn = (an[0] - bn[0]) || an[1].localeCompare(bn[1]);
        if (nn) return nn;
    }
    return ax.length - bx.length;
}

// Helper: Extract angka dari string "1, 2, 3" atau "5" dan push ke array (skip duplikat)
function extractNumbers(str, arr) {
    var parts = str.split(/[,\s]+/);
    for (var i = 0; i < parts.length; i++) {
        var clean = parts[i].replace(/\D/g, '');
        if (clean) {
            var num = parseInt(clean, 10);
            var exists = false;
            for (var j = 0; j < arr.length; j++) { if (arr[j] == num) { exists = true; break; } }
            if (!exists) arr.push(num);
        }
    }
}

function parseRevisionNumbers(text) {
    var numbers = [];

    // Step 1: Normalize — pecah "- " (dash-space) inline jadi newline
    //   "GRADASI- 1 MULUSIN" → "GRADASI\n1 MULUSIN"
    var normalized = text.replace(/-\s/g, '\n');

    // Step 2: Pecah jadi baris
    var lines = normalized.split(/[\r\n]+/);

    for (var i = 0; i < lines.length; i++) {
        var line = lines[i].replace(/^\s+/, ''); // Trim left
        if (!line) continue;

        // A. Keyword eksplisit: NO 1, NOMOR 2, NO. 3, #4, NOMOR 16,34
        var keywordMatch = line.match(/(?:NO\.?|NOMOR|NUM|#)\s*([\d\s,]+)/i);
        if (keywordMatch && keywordMatch[1]) {
            extractNumbers(keywordMatch[1], numbers);
            continue;
        }

        // B. Angka di awal segment (termasuk deret koma: "1, 2, 3, 5 ILANGIN")
        var startMatch = line.match(/^([\d][\d,\s]*)/);
        if (startMatch && startMatch[1]) {
            extractNumbers(startMatch[1], numbers);
        }
    }

    return numbers;
}

function isFileMatchNumber(file, number) {
    var name = decodeURI(file.name);
    var match = name.match(/^(\d+)/);
    if (match && parseInt(match[1], 10) === number) return true;
    return false;
}

function main() {
    var settings = loadSettings();

    // === DIALOG 1: PILIH FOLDER ===
    var dlg_main = new Window("dialog", "Pilih Folder Master");
    dlg_main.orientation = "column";
    dlg_main.alignChildren = ["fill", "top"];

    var grpFolder = dlg_main.add("group");
    grpFolder.add("statictext", undefined, "Folder:");
    var txtFolder = grpFolder.add("edittext", undefined, decodeURI(settings.lastPath));
    txtFolder.preferredSize.width = 300;
    var btnBrowse = grpFolder.add("button", undefined, "Browse...");

    btnBrowse.onClick = function () {
        var f = Folder(txtFolder.text || settings.lastPath).selectDlg("Pilih Folder Master");
        if (f) txtFolder.text = decodeURI(f.fullName);
    };

    var btnGroup = dlg_main.add("group");
    btnGroup.add("button", undefined, "Lanjut", { name: "ok" });
    btnGroup.add("button", undefined, "Batal", { name: "cancel" });

    if (settings.main_x > 0) dlg_main.location = [settings.main_x, settings.main_y];
    else dlg_main.center();

    if (dlg_main.show() != 1) return;

    var rawPath = txtFolder.text.replace(/^["']+|["']+$/g, "").replace(/^\s+|\s+$/g, "");
    settings.lastPath = rawPath;
    settings.main_x = dlg_main.location.x;
    settings.main_y = dlg_main.location.y;
    saveSettings(settings);

    var folder = new Folder(settings.lastPath);
    if (!folder.exists) { alert("Folder tidak ada!"); return; }

    var allPsdFiles = getPSDfiles(folder);
    if (allPsdFiles.length === 0) { alert("Tidak ada PSD/PSB!"); return; }
    allPsdFiles.sort(naturalSort);

    // === DIALOG 2: LIST FILE & FILTER ===
    var dlg_list = new Window("dialog", "Pilih File PSD/PSB");
    dlg_list.orientation = "column";
    dlg_list.alignChildren = ["fill", "fill"];

    if (settings.list_x > 0) dlg_list.location = [settings.list_x, settings.list_y];
    else dlg_list.center();

    var grpContent = dlg_list.add("group");
    grpContent.orientation = "row";
    grpContent.alignChildren = ["fill", "fill"];
    grpContent.spacing = 15;

    // Kiri
    var pnlLeft = grpContent.add("panel", undefined, "List File PSD/PSB");
    pnlLeft.orientation = "column";
    pnlLeft.alignChildren = ["fill", "fill"];
    pnlLeft.preferredSize = [350, 450];
    var list = pnlLeft.add("listbox", undefined, [], { multiselect: true });
    list.preferredSize.height = 400;

    // Kanan
    var pnlRight = grpContent.add("panel", undefined, "Filter Revisi (Paste Chat/Trello)");
    pnlRight.orientation = "column";
    pnlRight.alignChildren = ["fill", "fill"];
    pnlRight.preferredSize = [350, 450];
    var txtFilter = pnlRight.add("edittext", undefined, "", { multiline: true });
    txtFilter.preferredSize.height = 350;
    txtFilter.helpTip = "Paste revisi di sini (contoh: NO 1 revisi ini...)";

    var grpFilterControls = pnlRight.add("group");
    grpFilterControls.orientation = "column";
    grpFilterControls.alignChildren = ["fill", "top"];
    var btnApplyFilter = grpFilterControls.add("button", undefined, "FILTER List");
    var btnReset = grpFilterControls.add("button", undefined, "Reset List (Show All)");
    var chkSort = grpFilterControls.add("checkbox", undefined, "Urutkan Nomor (Ascending)");
    chkSort.value = settings.sort_asc;

    function populateList(files, autoSelectAll) {
        list.removeAll();
        for (var i = 0; i < files.length; i++) {
            var label = decodeURI(files[i].parent.name) + "/" + decodeURI(files[i].name);
            var item = list.add("item", label);
            item.fileRef = files[i];
            if (autoSelectAll) item.selected = true;
        }
    }
    populateList(allPsdFiles, false);

    // --- LOGIC FILTER ---
    // Helper: Cek apakah nama folder cocok dengan kontekstual tokens
    function isFolderMatchContext(folderName, contextTokens) {
        if (!contextTokens || contextTokens.length === 0) return true; // No context = match all
        var nameUpper = folderName.toUpperCase();
        for (var i = 0; i < contextTokens.length; i++) {
            if (nameUpper.indexOf(contextTokens[i]) >= 0) return true;
        }
        return false;
    }

    // Helper: Cari nama folder unik dari file list untuk pre-scan
    function getUniqueFolderKeywords(files) {
        var folders = {};
        for (var i = 0; i < files.length; i++) {
            if (files[i].parent) {
                var pName = decodeURI(files[i].parent.name).toUpperCase();
                // Bersihkan nama folder: KELAS BILAL BIN RABBAH (B1) -> BILAL BIN RABBAH
                // Buang kata "KELAS", "CLASS", dan tanda kurung "(B1)"
                var clean = pName.replace(/KELAS|CLASS|\(B\d+\)|\(.*\)/g, "").replace(/^\s+|\s+$/g, "");

                // Pastikan keyword cukup panjang (> 3 huruf) agar tidak false positive
                if (clean.length > 3) folders[clean] = true;
            }
        }
        var keywords = [];
        for (var k in folders) keywords.push(k);
        // Sort by length desc (dahulukan nama panjang agar tidak tertimpa nama pendek)
        keywords.sort(function (a, b) { return b.length - a.length });
        return keywords;
    }

    btnApplyFilter.onClick = function () {
        var text = txtFilter.text;
        if (!text) { alert("Paste teks revisi dulu!"); return; }

        // PRE-PROCESS: Inject Newline sebelum Folder Keywords yang ditemukan di teks
        // Ini menangani kasus teks menyambung tanpa enter: "...ANAKABU BAKAR..."
        var folderKeywords = getUniqueFolderKeywords(allPsdFiles);
        var processedText = text.toUpperCase().replace(/-\s/g, '\n'); // Normalize dash first

        for (var k = 0; k < folderKeywords.length; k++) {
            var kw = folderKeywords[k];
            if (processedText.indexOf(kw) >= 0) {
                // Ganti semua kemunculan KEYWORD dengan "\nKEYWORD\n"
                // Gunakan split-join karena String.replace(string) hanya replace first match di extendscript standard
                processedText = processedText.split(kw).join("\n" + kw + "\n");
            }
        }

        var lines = processedText.split(/[\r\n]+/);


        var currentContextTokens = [];
        var foundAny = false;
        var filteredFiles = [];

        // Loop baris per baris untuk maintain urutan dan konteks
        for (var i = 0; i < lines.length; i++) {
            var line = lines[i].replace(/^\s+/, '').replace(/\s+$/, '');
            if (!line) continue;

            // Cek apakah baris ini berisi angka revisi?
            // Pola: "NO 1...", "1, 2...", "- 1...", "• 1..."
            var isNumberLine = false;
            var numbersInLine = [];

            // Cek Keyword/Pola Angka
            if (line.match(/^(?:NO\.?|NOMOR|NUM|#|[-*•]|\d)/i)) {
                // Ini kemungkinan baris data, coba extract angka
                // Logic extractNumbers manual di sini agar clean
                var parts = line.split(/[,\s]+/);
                for (var p = 0; p < parts.length; p++) {
                    var clean = parts[p].replace(/\D/g, '');
                    if (clean && parts[p].length < 5) { // Angka valid < 5 digit (thn 2024 skip)
                        numbersInLine.push(parseInt(clean, 10));
                    }
                }
                if (numbersInLine.length > 0) isNumberLine = true;
            }

            if (!isNumberLine) {
                // UPDATE CONTEXT HEADER
                // Ambil kata-kata signifikan (> 2 huruf) dari baris ini sebagai konteks folder
                // Contoh: "BILAL BIN RABAH" -> ["BILAL", "BIN", "RABAH"]
                var tokens = line.toUpperCase().split(/[^A-Z0-9]+/);
                var validTokens = [];
                for (var t = 0; t < tokens.length; t++) {
                    if (tokens[t].length > 2) validTokens.push(tokens[t]);
                }

                // Jika found valid tokens, update context. Kalau baris kosong/simbol, keep old context
                if (validTokens.length > 0) {
                    currentContextTokens = validTokens;
                }
            } else {
                // PROCESS NUMBERS WITH CURRENT CONTEXT
                for (var n = 0; n < numbersInLine.length; n++) {
                    var targetNum = numbersInLine[n];
                    var backupMatch = null;
                    var foundContextMatch = false;

                    // Cari file yang cocok dengan nomor ini
                    for (var f = 0; f < allPsdFiles.length; f++) {
                        if (isFileMatchNumber(allPsdFiles[f], targetNum)) {
                            // Simpan match pertama sebagai cadangan (jika tidak ada context match)
                            if (!backupMatch) backupMatch = allPsdFiles[f];

                            var folderName = decodeURI(allPsdFiles[f].parent.name);
                            // Cek Context
                            if (isFolderMatchContext(folderName, currentContextTokens)) {
                                var bestMatch = allPsdFiles[f];
                                foundContextMatch = true;

                                // Cek duplikat exact file path
                                var already = false;
                                for (var k = 0; k < filteredFiles.length; k++) {
                                    if (filteredFiles[k].fullName === bestMatch.fullName) already = true;
                                }
                                if (!already) {
                                    filteredFiles.push(bestMatch);
                                }
                            }
                        }
                    }

                    // FALLBACK: Jika tidak ketemu context match, pakai backupMatch (first match)
                    if (!foundContextMatch && backupMatch) {
                        var already = false;
                        for (var k = 0; k < filteredFiles.length; k++) {
                            if (filteredFiles[k].fullName === backupMatch.fullName) already = true;
                        }
                        if (!already) filteredFiles.push(backupMatch);
                    }
                }
            }
        }

        if (filteredFiles.length === 0) { alert("Tidak ada file yang cocok dengan kriteria!"); return; }

        // Sorting
        if (chkSort.value) filteredFiles.sort(naturalSort);

        populateList(filteredFiles, true);
    };

    btnReset.onClick = function () {
        txtFilter.text = "";
        populateList(allPsdFiles, false);
    };

    // --- BUTTONS BAWAH ---
    var grpButtons = dlg_list.add("group");
    grpButtons.alignment = "center";

    // Explicit reference untuk handle onClick
    var btnCancel = grpButtons.add("button", undefined, "Batal", { name: "cancel" });
    var btnOpen = grpButtons.add("button", undefined, "Buka (Selected)", { name: "ok" });
    var btnOpenAll = grpButtons.add("button", undefined, "Buka Semua (List)");

    // Override onClick to ensure correct return codes
    // 0: Cancel, 1: Selected, 3: All List Items
    btnCancel.onClick = function () { dlg_list.close(0); };
    btnOpen.onClick = function () { dlg_list.close(1); };
    btnOpenAll.onClick = function () { dlg_list.close(3); };

    // Show
    var result = dlg_list.show();

    settings.list_x = dlg_list.location.x;
    settings.list_y = dlg_list.location.y;
    settings.sort_asc = chkSort.value;
    saveSettings(settings); // Selalu simpan setting walau cancel

    // Jika result bukan 1 (Selected) atau 3 (All), alias 0/2 (Cancel/Close window), maka stop.
    if (result !== 1 && result !== 3) return;

    var filesToOpen = [];

    if (result === 1) { // OPEN SELECTED
        var items = list.selection;
        if (items) {
            for (var i = 0; i < items.length; i++) filesToOpen.push(items[i].fileRef);
        }
    } else if (result === 3) { // OPEN ALL LIST ITEMS
        for (var i = 0; i < list.items.length; i++) {
            filesToOpen.push(list.items[i].fileRef);
        }
    }

    if (filesToOpen.length == 0) return;

    for (var i = 0; i < filesToOpen.length; i++) {
        if (filesToOpen[i].exists) app.open(filesToOpen[i]);
    }
}

main();
