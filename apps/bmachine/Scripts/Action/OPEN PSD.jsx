// Photoshop JSX Script
// Buka banyak file PSD dari folder (natural sort A-Z + angka)
// Update: 2-Column Layout, Filter Revisi, Sort Options, Restore "Buka Semua", Fix Cancel Logic
// v3: Treeview grouped per-folder (path relatif ke root), filter revisi
//     per section + per nomor per-folder (nomor tidak global: 5 di B1 != 5 di B2)

#target photoshop

function getRelPath(file, rootFolder) {
    var root = decodeURI(rootFolder.fsName).replace(/\\/g, '/').toUpperCase();
    var p = decodeURI(file.fsName).replace(/\\/g, '/').toUpperCase();
    if (p.indexOf(root) === 0) p = p.substring(root.length);
    return p.replace(/^\//, '');
}

function normalizeFolderName(s) {
    return s.toUpperCase().replace(/[^A-Z0-9]/g, '');
}

// Styling aman: try/catch per property agar ScriptUI API beda versi tidak bikin error fatal
function styleLabel(el, family, size, bold, rgb) {
    try { el.graphics.font.name = family; } catch (e1) { }
    try { el.graphics.font.size = size; } catch (e2) { }
    try { el.graphics.font.weight = bold ? "bold" : "normal"; } catch (e3) { }
    try { el.graphics.foregroundFill = rgb; } catch (e4) { }
}

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

function isFileMatchNumber(file, number) {
    var name = decodeURI(file.name);
    // Support: "4 - retouch.psd", "(4) - retouch.psd", "[4] - retouch.psd"
    var match = name.match(/^[\(\[]?(\d+)[\)\]]?/);
    if (match && parseInt(match[1], 10) === number) return true;
    return false;
}

// Ekstrak semua nomor dari satu baris revisi.
// Pola A: keyword NO/NOMOR/NUM global di baris (termasuk tengah kalimat)
// Pola B: baris mulai dengan angka/list marker ("3.SHADOW", "- 4 revisi", "1. NO 7")
// Return: { numbers: [..unik..], isNumberLine: bool }
function extractLineNumbers(line) {
    var numbersInLine = [];
    var isNumberLine = false;

    var kwRe = /(?:NO\.?|NOMOR|NUM)\s*([\d\.,\s]+)/gi;
    var kwMatch;
    while ((kwMatch = kwRe.exec(line)) !== null) {
        var kwParts = kwMatch[1].split(/[\.,\s]+/);
        for (var p = 0; p < kwParts.length; p++) {
            var cln = kwParts[p].replace(/\D/g, '');
            if (cln && cln.length < 5) numbersInLine.push(parseInt(cln, 10));
        }
        if (kwRe.lastIndex === kwMatch.index) kwRe.lastIndex++;
    }
    if (numbersInLine.length > 0) isNumberLine = true;

    if (!isNumberLine && line.match(/^(?:[-*]\s*)?\d/)) {
        var subKwMatch = line.match(/^(?:[-*]\s*)?\d+[\.)]\s*(?:NO\.?|NOMOR|NUM|#)\s*([\d\.,\s]+)/i);
        if (subKwMatch && subKwMatch[1]) {
            var subParts = subKwMatch[1].split(/[\.,\s]+/);
            for (var p2 = 0; p2 < subParts.length; p2++) {
                var cln2 = subParts[p2].replace(/\D/g, '');
                if (cln2 && cln2.length < 5) numbersInLine.push(parseInt(cln2, 10));
            }
        } else if (!/(?:NO\.?|NOMOR|NUM|#)/i.test(line)) {
            var prefixMatch = line.match(/^(?:[-*]\s*)?([\d\.,\s]+)/);
            if (prefixMatch && prefixMatch[1]) {
                var bParts = prefixMatch[1].split(/[\.,\s]+/);
                for (var p3 = 0; p3 < bParts.length; p3++) {
                    var cln3 = bParts[p3].replace(/\D/g, '');
                    if (cln3 && cln3.length < 5) numbersInLine.push(parseInt(cln3, 10));
                }
            }
        }
        if (numbersInLine.length > 0) isNumberLine = true;
    }

    var uniq = [];
    for (var n = 0; n < numbersInLine.length; n++) {
        var found = false;
        for (var d = 0; d < uniq.length; d++) { if (uniq[d] === numbersInLine[n]) { found = true; break; } }
        if (!found) uniq.push(numbersInLine[n]);
    }
    return { numbers: uniq, isNumberLine: isNumberLine };
}

// Header section revisi: baris ALL-CAPS tanpa digit yang mulai "#",
// ATAU mengandung "REVISI", ATAU salah satu SECTION_KEYWORDS.
var SECTION_KEYWORDS = ["PROFESI", "MANASIK", "PAS FOTO", "FREE PAS FOTO", "FREE SEKOLAH", "FREE POSTER", "FREE KALENDER", "BACKDROP", "VARIOS", "SCRAP", "MUSIC", "ROCK", "LEGENDIA", "KANSAS", "REGGAE"];

function parseRevisiSections(revisiText) {
    var lines = revisiText.split(/\r?\n/);
    var sections = [];
    var current = null;

    for (var i = 0; i < lines.length; i++) {
        var line = lines[i].replace(/^\s+/, '').replace(/\s+$/, '');
        if (!line) continue;

        var isHeader = false;
        if (line === line.toUpperCase() && !line.match(/\d/)) {
            var isKwHeader = false;
            for (var k = 0; k < SECTION_KEYWORDS.length; k++) {
                if (line.indexOf(SECTION_KEYWORDS[k]) >= 0) { isKwHeader = true; break; }
            }
            if (line.charAt(0) === '#' || line.indexOf('REVISI') >= 0 || isKwHeader) {
                isHeader = true;
            }
        }

        if (isHeader) {
            current = { title: line, items: [] };
            sections.push(current);
        } else if (current) {
            current.items.push(line);
        }
    }
    return sections;
}

// Mapping judul section -> grup folder yang cocok (fuzzy, token + frasa).
// folderGroups = [{key, relPath, files}], return [{key, relPath, score, exact}]
function findMatchingFolders(sectionTitle, folderGroups) {
    var titleUpper = sectionTitle.toUpperCase();
    var titleClean = titleUpper.replace(/^#+/, '').replace(/^\s*REVISI\s*/, '');
    var stopWords = { "REVISI": 1, "PROYEK": 1, "THE": 1, "A": 1, "AN": 1, "OB": 1, "T": 1, "TM": 1 };
    var tokens = titleClean.split(/[^A-Z0-9]+/);
    var meaningfulTokens = [];
    for (var ti = 0; ti < tokens.length; ti++) {
        var tok = tokens[ti];
        if (tok.length >= 2 && !stopWords[tok]) meaningfulTokens.push(tok);
    }
    var phraseTokens = [];
    for (var pi = 0; pi < meaningfulTokens.length - 1; pi++) {
        phraseTokens.push(normalizeFolderName(meaningfulTokens[pi] + meaningfulTokens[pi + 1]));
    }

    var results = [];
    for (var g = 0; g < folderGroups.length; g++) {
        var gNorm = normalizeFolderName(folderGroups[g].relPath);
        var matchedTokenCount = 0;

        // Alias proyek umum. Banyak folder PROFESI memakai nama produk "OB SPORTY"
        // tanpa kata PROFESI; section "PROFESI" tetap harus masuk folder OB itu.
        if (titleUpper.indexOf("PROFESI") >= 0 && gNorm.indexOf("OB") >= 0) matchedTokenCount += 3;
        if (titleUpper.indexOf("MANASIK") >= 0 && gNorm.indexOf("MANASIK") >= 0) matchedTokenCount += 3;
        if (titleUpper.indexOf("FREE PAS FOTO") >= 0 && gNorm.indexOf("FREEPASFOTO") >= 0) matchedTokenCount += 3;

        for (var m = 0; m < meaningfulTokens.length; m++) {
            var tokNorm = normalizeFolderName(meaningfulTokens[m]);
            if (tokNorm.length >= 2 && gNorm.indexOf(tokNorm) >= 0) matchedTokenCount++;
        }
        for (var ph = 0; ph < phraseTokens.length; ph++) {
            if (phraseTokens[ph].length >= 4 && gNorm.indexOf(phraseTokens[ph]) >= 0) matchedTokenCount += 2;
        }
        var titleNorm = normalizeFolderName(titleClean);
        var hasExact = gNorm.indexOf(titleNorm) >= 0 || titleNorm.indexOf(gNorm) >= 0;

        if (matchedTokenCount > 0 || hasExact) {
            results.push({ key: folderGroups[g].key, relPath: folderGroups[g].relPath, score: matchedTokenCount, exact: hasExact });
        }
    }

    results.sort(function (a, b) {
        if (a.exact !== b.exact) return b.exact - a.exact;
        return b.score - a.score;
    });

    var topScore = results.length > 0 ? results[0].score : 0;
    var filtered = [];
    for (var r = 0; r < results.length; r++) {
        if (results[r].exact || results[r].score >= Math.ceil(topScore / 2)) {
            filtered.push(results[r]);
            if (filtered.length >= 4) break;
        }
    }
    return filtered;
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

    // === GROUPING: file per folder leaf, key = path relatif folder terhadap root ===
    var folderGroups = [];
    var groupMap = {};
    for (var gi = 0; gi < allPsdFiles.length; gi++) {
        var gf = allPsdFiles[gi];
        var leafRel = getRelPath(gf, folder);
        var dirRel = leafRel.substring(0, leafRel.lastIndexOf('/'));
        var gkey = (dirRel === "") ? "(ROOT)" : dirRel;
        var gkeyNorm = normalizeFolderName(gkey);
        if (!(gkeyNorm in groupMap)) {
            groupMap[gkeyNorm] = folderGroups.length;
            folderGroups.push({ key: gkey, relPath: gkey, files: [] });
        }
        folderGroups[groupMap[gkeyNorm]].files.push(gf);
    }
    folderGroups.sort(function (a, b) {
        var ax = [], bx = [];
        a.relPath.replace(/(\d+)|(\D+)/g, function (_, $1, $2) { ax.push([$1 || Infinity, $2 || ""]); });
        b.relPath.replace(/(\d+)|(\D+)/g, function (_, $1, $2) { bx.push([$1 || Infinity, $2 || ""]); });
        while (ax.length && bx.length) {
            var an = ax.shift(), bn = bx.shift();
            var nn = (an[0] - bn[0]) || an[1].localeCompare(bn[1]);
            if (nn) return nn;
        }
        return ax.length - bx.length;
    });

    // === DIALOG 2: LIST FILE & FILTER ===
    var dlg_list = new Window("dialog", "Pilih File PSD/PSB");
    dlg_list.orientation = "column";
    dlg_list.alignChildren = ["fill", "fill"];
    dlg_list.margins = 10;
    dlg_list.spacing = 8;

    if (settings.list_x > 0) dlg_list.location = [settings.list_x, settings.list_y];
    else dlg_list.center();

    // Header: path root
    var grpContent = dlg_list.add("group");
    grpContent.orientation = "column";
    grpContent.alignChildren = ["fill", "fill"];
    grpContent.spacing = 8;

    var rootInfo = grpContent.add("group");
    rootInfo.orientation = "row";
    rootInfo.alignChildren = ["fill", "center"];
    rootInfo.spacing = 8;
    var rootLbl = rootInfo.add("statictext", undefined, "Root:");
    styleLabel(rootLbl, "Segoe UI", 10, false, [50, 50, 50]);
    var rootPathTxt = rootInfo.add("statictext", undefined, decodeURI(folder.fsName));
    styleLabel(rootPathTxt, "Segoe UI", 10, false, [120, 120, 120]);

    var grpCols = grpContent.add("group");
    grpCols.orientation = "row";
    grpCols.alignChildren = ["fill", "fill"];
    grpCols.spacing = 10;

    // ===== KIRI: LISTBOX native, scrollbar asli Photoshop =====
    // Folder dibuat sebagai header, file di bawahnya dibuat indent. Tidak ada checkbox,
    // collapse, atau expand. Pilih file dengan Ctrl+klik bila perlu lebih dari satu.
    var pnlLeft = grpCols.add("panel", undefined, "File PSD/PSB per Folder");
    pnlLeft.orientation = "column";
    pnlLeft.alignChildren = ["fill", "fill"];
    pnlLeft.margins = 8;
    pnlLeft.spacing = 4;
    pnlLeft.preferredSize = [300, 355];
    pnlLeft.maximumSize = [300, 355];

    var listInfo = pnlLeft.add("statictext", undefined, "Folder dipisah di daftar. Ctrl+klik untuk pilih beberapa file.");
    styleLabel(listInfo, "Segoe UI", 8, false, [100, 100, 100]);

    var fileList = pnlLeft.add("listbox", undefined, [], { multiselect: true });
    fileList.preferredSize = [275, 270];
    fileList.minimumSize = [275, 270];
    fileList.maximumSize = [275, 270];

    var treeItems = []; // [{header, files, grp}]

    // Listbox native membuat horizontal scrollbar saat teks terlalu panjang.
    // Potong hanya tampilan, data/path file tetap utuh di fileRef + helpTip.
    function compactListText(text, maxChars) {
        if (text.length <= maxChars) return text;
        return text.substring(0, maxChars - 3) + "...";
    }

    function isShownFile(fileObj, filesToShow) {
        for (var i = 0; i < filesToShow.length; i++) {
            if (filesToShow[i].fullName === fileObj.fullName) return true;
        }
        return false;
    }

    function buildTree(filesToShow, unusedExpand, statusNoteByFolder) {
        fileList.removeAll();
        treeItems = [];

        for (var g = 0; g < folderGroups.length; g++) {
            var grp = folderGroups[g];
            // Ambil menurut urutan filesToShow, bukan urutan nama folder.
            // Saat hasil filter, filesToShow sudah disusun sesuai nomor revisi.
            var visibleFiles = [];
            for (var ff = 0; ff < filesToShow.length; ff++) {
                for (var gf = 0; gf < grp.files.length; gf++) {
                    if (grp.files[gf].fullName === filesToShow[ff].fullName) {
                        visibleFiles.push(grp.files[gf]);
                        break;
                    }
                }
            }
            if (visibleFiles.length === 0) continue;

            var folderName = decodeURI(grp.key === "(ROOT)" ? "(ROOT)" : grp.relPath);
            var note = statusNoteByFolder ? statusNoteByFolder[grp.key] : "";
            var headerFullText = "[ " + folderName + " ]  " + visibleFiles.length + " file";
            if (note) headerFullText += " - " + note;
            var header = fileList.add("item", compactListText(headerFullText, 39));
            header.helpTip = headerFullText;
            header.isFolderHeader = true;
            header.enabled = false;

            var fileItems = [];
            for (var fv = 0; fv < visibleFiles.length; fv++) {
                var name = decodeURI(visibleFiles[fv].name);
                var numMatch = name.match(/^[\(\[]?(\d+)[\)\]]?/);
                var labelFullText = "    " + name + (numMatch ? "   #" + numMatch[1] : "");
                var item = fileList.add("item", compactListText(labelFullText, 40));
                item.fileRef = visibleFiles[fv];
                item.helpTip = decodeURI(getRelPath(visibleFiles[fv], folder));
                fileItems.push(item);
            }
            treeItems.push({ header: header, children: fileItems, files: visibleFiles, grp: grp });
        }
        updateSelectionCount();
        try { fileList.layout.layout(true); dlg_list.layout.layout(true); } catch (eLayout) { }
    }

    function getSelectedFiles() {
        var selected = fileList.selection;
        if (!selected) return [];
        if (!(selected instanceof Array)) selected = [selected];
        var out = [];
        for (var i = 0; i < selected.length; i++) {
            if (selected[i].fileRef) out.push(selected[i].fileRef);
        }
        return out;
    }

    function selectAllVisibleFiles() {
        var selected = [];
        for (var g = 0; g < treeItems.length; g++) {
            for (var f = 0; f < treeItems[g].children.length; f++) selected.push(treeItems[g].children[f]);
        }
        fileList.selection = selected;
        updateSelectionCount();
    }

    function updateSelectionCount() {
        var total = 0;
        for (var g = 0; g < treeItems.length; g++) total += treeItems[g].files.length;
        var selectedFiles = getSelectedFiles();
        pnlLeft.text = selectedFiles.length > 0
            ? "File PSD/PSB - " + selectedFiles.length + " dipilih / " + total + " file (" + treeItems.length + " folder)"
            : "File PSD/PSB - " + total + " file (" + treeItems.length + " folder)";
    }

    fileList.onChange = updateSelectionCount;

    // ===== KANAN: FILTER =====
    var pnlRight = grpCols.add("panel", undefined, "Filter Revisi (Tempel dari Chat/Trello)");
    pnlRight.orientation = "column";
    pnlRight.alignChildren = ["fill", "fill"];
    pnlRight.margins = 8;
    pnlRight.spacing = 6;
    pnlRight.preferredSize = [280, 355];
    pnlRight.maximumSize = [280, 355];

    var filterHint = pnlRight.add("statictext", undefined,
        "Judul (PROFESI / FREE SEKOLAH) = folder\n"
        + "NO 1, NO 2 = nomor file\n"
        + "Tanpa nomor = semua file");
    styleLabel(filterHint, "Segoe UI", 8, false, [100, 100, 100]);

    var txtFilter = pnlRight.add("edittext", undefined, "", { multiline: true, scrollbar: "auto" });
    txtFilter.preferredSize = [250, 130];
    txtFilter.maximumSize = [260, 500];
    txtFilter.helpTip = "Paste revisi di sini";

    // Auto-format for pasted text that loses newlines
    var prevText = "";
    txtFilter.onChanging = function() {
        var currentText = txtFilter.text;

        // Deteksi apakah user melakukan Paste (perubahan karakter banyak sekaligus)
        var isPasted = Math.abs(currentText.length - prevText.length) > 5;

        if (isPasted) {
            var newText = currentText;

            // 1. Teks menyatu tapi angka memiliki titik (contoh: "onta3.retouch" atau "onta 3.")
            //    SKIP jika kata sebelum titik adalah keyword: "NO 5." -> jangan pecah
            //    FIX: prefix ditangkap sebagai KATA UTUH ("NO"), bukan 1 huruf ("O")
            newText = newText.replace(/([a-zA-Z]+)\s*(\d+\.)/g, function(match, prefix, numDot) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n" + numDot;
            });

            // 2. Teks menyatu tanpa titik, diikuti aksi (contoh: "anak3 hilangkan" atau "anak 15, 19 hilangkan")
            //    SKIP jika kata sebelum angka adalah keyword: "NO 5 TOLONG" / "NO1" -> jangan pecah
            //    FIX: prefix ditangkap sebagai KATA UTUH ("NO"), guard kemudian cocok.
            //    Dulu prefix 1 huruf ("O") -> guard gagal -> "NO 5" terbelah jadi "1. NO" + "5 ..."
            //    -> baris "1. NO" jatuh ke Pola B2 dan angka INDEX (1,2,3..) ikut termasuk.
            newText = newText.replace(/([a-zA-Z]+)\s*(?=\d+(?:[,\s]+\d+)*\s+[a-zA-Z])/g, function(match, prefix) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n";
            });

            // 3. Pisahkan teks yang menyatu dengan strip (contoh: "lagi- 4 serabut")
            //    SKIP jika kata sebelum strip adalah keyword: "NO- 4" -> jangan pecah
            //    FIX: prefix kata utuh supaya guard "NO" cocok (dulu 1 huruf, guard selalu gagal)
            newText = newText.replace(/([^\s\r\n]+)\s*\s*-\s*(\d+)/g, function(match, prefix, num) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n- " + num;
            });


            // 5. Merge: baris yang BERAKHIRAN keyword ("NO", "1. NO", "NOMOR") + baris angka
            //    berikutnya -> gabung (menangani paste yang memecah "1. NO" / "NO" dari angkanya)
            //    Proses baris per baris agar aman (tidak pakai regex global yang salah)
            var arr = newText.split(/\r?\n/);
            var out = [];
            for (var i = 0; i < arr.length; i++) {
                var t = arr[i].replace(/^\s+/, '').replace(/\s+$/, '');
                var isKwTail = /(?:NO\.?|NOMOR|NUM|REV|ULANG)$/i.test(t) || /^(?:PERBAIKI|UBAH)$/i.test(t);
                if (isKwTail && i + 1 < arr.length) {
                    var next = arr[i + 1].replace(/^\s+/, '');
                    if (/^\d/.test(next)) {
                        out.push(t + " " + next);
                        i++;
                        continue;
                    }
                }
                out.push(arr[i]);
            }
            newText = out.join("\r\n");

            if (newText !== currentText) {
                txtFilter.text = newText;
                currentText = newText;
            }
        }
        prevText = currentText;
    };

    // Tiga aksi revisi berada di panel Filter, tepat bawah kotak paste.
    var grpFilterControls = pnlRight.add("group");
    grpFilterControls.orientation = "column";
    grpFilterControls.alignChildren = ["fill", "top"];
    grpFilterControls.spacing = 4;
    var btnApplyFilter = grpFilterControls.add("button", undefined, "Filter List (Section + Nomor)");
    var btnReset = grpFilterControls.add("button", undefined, "Reset (Tampilkan Semua)");
    var btnAutoRevisi = grpFilterControls.add("button", undefined, "Baca & Buka Otomatis");

    var filterStatusBox = pnlRight.add("group");
    filterStatusBox.orientation = "column";
    filterStatusBox.alignChildren = ["fill", "top"];
    filterStatusBox.margins = 4;
    var filterStatusLabel = filterStatusBox.add("statictext", undefined, "Siap. Tempel revisi di kolom atas, lalu klik Filter.");
    styleLabel(filterStatusLabel, "Segoe UI", 9, false, [80, 80, 80]);

    function setFilterStatus(msg, colorRGB) {
        filterStatusLabel.text = msg;
        styleLabel(filterStatusLabel, "Segoe UI", 9, false, colorRGB || [80, 80, 80]);
    }

    // Auto-format for pasted text that loses newlines
    var prevText = "";
    txtFilter.onChanging = function() {
        var currentText = txtFilter.text;
        var isPasted = Math.abs(currentText.length - prevText.length) > 5;
        if (isPasted) {
            var newText = currentText;
            newText = newText.replace(/([a-zA-Z]+)\s*(\d+\.)/g, function(match, prefix, numDot) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n" + numDot;
            });
            newText = newText.replace(/([a-zA-Z]+)\s*(?=\d+(?:[,\s]+\d+)*\s+[a-zA-Z])/g, function(match, prefix) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n";
            });
            newText = newText.replace(/([^\s\r\n]+)\s*\s*-\s*(\d+)/g, function(match, prefix, num) {
                var upper = prefix.toUpperCase();
                if (upper.match(/(?:NO|NOMOR|NUM|REV|ULANG)$/)) return match;
                return prefix + "\r\n- " + num;
            });
            var arr = newText.split(/\r?\n/);
            var out = [];
            for (var i = 0; i < arr.length; i++) {
                var t = arr[i].replace(/^\s+/, '').replace(/\s+$/, '');
                var isKwTail = /(?:NO\.?|NOMOR|NUM|REV|ULANG)$/i.test(t) || /^(?:PERBAIKI|UBAH)$/i.test(t);
                if (isKwTail && i + 1 < arr.length) {
                    var next = arr[i + 1].replace(/^\s+/, '');
                    if (/^\d/.test(next)) {
                        out.push(t + " " + next);
                        i++;
                        continue;
                    }
                }
                out.push(arr[i]);
            }
            newText = out.join("\r\n");
            if (newText !== currentText) {
                txtFilter.text = newText;
                currentText = newText;
            }
        }
        prevText = currentText;
    };

    buildTree(allPsdFiles, true);

    // --- LOGIC FILTER BARU: section + nomor per-folder ---
    // buildFilterPlan: parse revisi -> per section, mapping ke folderGroups,
    //                  ekstrak nomor -> plan { [keyNorm]: { key, numbers: []|null } }
    // numbers = null berarti "semua file di folder itu".
    function buildFilterPlan(filterText) {
        var plan = {};
        var unmatched = [];
        var sections = parseRevisiSections(filterText);

        if (sections.length === 0) {
            // Tanpa section header: kumpulkan semua nomor dari semua baris,
            // terapkan ke SEMUA folder (fallback).
            var allNums = [];
            var lines = filterText.split(/\r?\n/);
            for (var i = 0; i < lines.length; i++) {
                var r = extractLineNumbers(lines[i].replace(/^\s+/, '').replace(/\s+$/, ''));
                if (r.isNumberLine) {
                    for (var n = 0; n < r.numbers.length; n++) {
                        var exists = false;
                        for (var d = 0; d < allNums.length; d++) if (allNums[d] === r.numbers[n]) { exists = true; break; }
                        if (!exists) allNums.push(r.numbers[n]);
                    }
                }
            }
            if (allNums.length === 0) return { plan: null, hasSection: false };
            for (var g = 0; g < folderGroups.length; g++) {
                plan[normalizeFolderName(folderGroups[g].key)] = { key: folderGroups[g].key, numbers: allNums };
            }
            return { plan: plan, hasSection: false };
        }

        for (var s = 0; s < sections.length; s++) {
            var sec = sections[s];
            var matched = findMatchingFolders(sec.title, folderGroups);
            if (matched.length === 0) { unmatched.push(sec.title); continue; }

            // Kumpulkan nomor unik di section ini
            var secNums = [];
            var secHasNumber = false;
            for (var it = 0; it < sec.items.length; it++) {
                var rr = extractLineNumbers(sec.items[it]);
                if (rr.isNumberLine) {
                    secHasNumber = true;
                    for (var n2 = 0; n2 < rr.numbers.length; n2++) {
                        var ex2 = false;
                        for (var d2 = 0; d2 < secNums.length; d2++) if (secNums[d2] === rr.numbers[n2]) { ex2 = true; break; }
                        if (!ex2) secNums.push(rr.numbers[n2]);
                    }
                }
            }

            for (var mf = 0; mf < matched.length; mf++) {
                var keyNorm = normalizeFolderName(matched[mf].key);
                if (!(keyNorm in plan)) {
                    // Section dengan nomor: mulai daftar kosong, lalu isi nomor
                    // Section tanpa nomor: null = semua file di folder
                    plan[keyNorm] = { key: matched[mf].key, numbers: secHasNumber ? [] : null };
                }
                if (secHasNumber && plan[keyNorm].numbers !== null) {
                    for (var nu = 0; nu < secNums.length; nu++) {
                        var ex3 = false;
                        for (var d3 = 0; d3 < plan[keyNorm].numbers.length; d3++) if (plan[keyNorm].numbers[d3] === secNums[nu]) { ex3 = true; break; }
                        if (!ex3) plan[keyNorm].numbers.push(secNums[nu]);
                    }
                }
            }
        }
        return { plan: plan, hasSection: true, unmatched: unmatched };
    }

    // Ambil file yang cocok dari satu entri plan
    function filesForPlanEntry(entry) {
        var grp = null;
        for (var g = 0; g < folderGroups.length; g++) {
            if (normalizeFolderName(folderGroups[g].key) === normalizeFolderName(entry.key)) { grp = folderGroups[g]; break; }
        }
        if (!grp) return [];
        if (entry.numbers === null) return grp.files;
        // Nomor revisi menentukan urutan tampilan/buka, bukan urutan nama file.
        // Contoh teks: 1,2,3,4,6 lalu 5,7,9,13 tetap tampil persis urutan itu.
        var out = [];
        for (var n = 0; n < entry.numbers.length; n++) {
            for (var f = 0; f < grp.files.length; f++) {
                if (isFileMatchNumber(grp.files[f], entry.numbers[n])) {
                    out.push(grp.files[f]);
                    break;
                }
            }
        }
        return out;
    }

    var unmatchedSections = null;
    function folderGroupNames() {
        var out = [];
        for (var g = 0; g < folderGroups.length; g++) out.push(folderGroups[g].relPath);
        return out;
    }

    // Apply plan ke treeview
    function applyPlanToList(plan, hasSection) {
        var shownFiles = [];
        var entryKeys = [];
        for (var k in plan) entryKeys.push(k);
        for (var e = 0; e < entryKeys.length; e++) {
            var fl = filesForPlanEntry(plan[entryKeys[e]]);
            for (var f = 0; f < fl.length; f++) shownFiles.push(fl[f]);
        }
        if (shownFiles.length === 0) {
            setFilterStatus("GAGAL: Tidak ada file yang cocok.", [200, 60, 60]);
            var extra = "";
            if (unmatchedSections && unmatchedSections.length > 0) {
                extra = "\n\nSection yang TIDAK ketemu folder:\n  - " + unmatchedSections.join("\n  - ") +
                    "\n\nNama folder di root ini:\n  - " + folderGroupNames().join("\n  - ");
            } else if (hasSection) {
                extra = "\n\nCek: judul section harus cocok dengan nama folder.\nNomor harus ada di awal nama file (mis. 5.psd).";
            } else {
                extra = "\nNomor harus ada di awal nama file (mis. 5.psd).";
            }
            alert("Tidak ada file yang cocok." + extra);
            return;
        }

        // Status note per folder: nomor apa yang diminta
        var statusNoteByFolder = {};
        for (var e2 = 0; e2 < entryKeys.length; e2++) {
            var ent = plan[entryKeys[e2]];
            var note = ent.numbers === null ? "semua file" : "nomor: " + ent.numbers.join(", ");
            statusNoteByFolder[ent.key] = note;
        }

        buildTree(shownFiles, true, statusNoteByFolder);

        // Otomatis pilih semua hasil filter (TreeView native)
        selectAllVisibleFiles();
        updateSelectionCount();

        setFilterStatus("OK: " + shownFiles.length + " file di " + treeItems.length + " folder" + (hasSection ? " (per section)" : ""), [40, 130, 40]);
        // Setelah filter sukses, tombol bawah jelas membuka hasil filter saja.
        btnOpenAll.text = "Buka Sesuai List";
        btnOpenAll.helpTip = "Buka hanya file yang sedang tampil dari hasil filter.";
    }

    // === SECTION SELECTOR: dialog ceklis section -> buka semua file di folder yang cocok ===
    function showSectionSelector(sections) {
        if (sections.length === 0) {
            alert("Tidak ada section yang terdeteksi dari revisi!\nGanti header revisi jadi pola: REVISI <NAMA PROYEK> (contoh: REVISI OB PROFESI, REVISI FREE SEKOLAH).");
            return;
        }

        var secDlg = new Window("dialog", "Pilih Section Revisi");
        secDlg.orientation = "column";
        secDlg.alignChildren = ["fill", "top"];
        secDlg.preferredSize = [400, 250];

        var grpSec = secDlg.add("group");
        grpSec.orientation = "column";
        grpSec.alignChildren = ["fill", "fill"];
        grpSec.preferredSize = [360, 170];

        var chkItems = [];
        for (var s = 0; s < sections.length; s++) {
            var matchedFolders = findMatchingFolders(sections[s].title, folderGroups);
            var folderLabel = matchedFolders.length > 0 ? matchedFolders[0].relPath : "(folder tidak ketemu)";
            var chk = grpSec.add("checkbox", undefined, sections[s].title + "  ->  " + folderLabel);
            chk.sectionIndex = s;
            chk.value = true;
            chkItems.push(chk);
        }

        var secBtnGroup = secDlg.add("group");
        secBtnGroup.alignment = "center";
        var secBtnOk = secBtnGroup.add("button", undefined, "Buka Terpilih", { name: "ok" });
        var secBtnCancel = secBtnGroup.add("button", undefined, "Batal", { name: "cancel" });
        secBtnOk.onClick = function () { secDlg.close(1); };
        secBtnCancel.onClick = function () { secDlg.close(0); };

        var secResult = secDlg.show();
        if (secResult !== 1) return;

        var filesToOpen = [];
        for (var s2 = 0; s2 < chkItems.length; s2++) {
            if (!chkItems[s2].value) continue;
            var sec = sections[s2];
            var matched = findMatchingFolders(sec.title, folderGroups);
            for (var mf = 0; mf < matched.length; mf++) {
                var grp = null;
                for (var g = 0; g < folderGroups.length; g++) {
                    if (normalizeFolderName(folderGroups[g].key) === normalizeFolderName(matched[mf].key)) { grp = folderGroups[g]; break; }
                }
                if (!grp) continue;
                for (var f = 0; f < grp.files.length; f++) filesToOpen.push(grp.files[f]);
            }
        }

        if (filesToOpen.length === 0) {
            alert("Tidak ada file yang akan dibuka untuk section yang dipilih.");
            return;
        }

        var successList = [], failList = [];
        for (var i = 0; i < filesToOpen.length; i++) {
            try {
                if (filesToOpen[i].exists) {
                    app.open(filesToOpen[i]);
                    successList.push(decodeURI(getRelPath(filesToOpen[i], folder)));
                } else {
                    failList.push(decodeURI(getRelPath(filesToOpen[i], folder)) + " (File tidak ditemukan)");
                }
            } catch (e) {
                failList.push(decodeURI(getRelPath(filesToOpen[i], folder)) + " (Error: " + e.message + ")");
            }
        }

        var msg = "Berhasil dibuka: " + successList.length + "\n";
        if (successList.length > 0) msg += successList.join("\n") + "\n\n";
        msg += "Gagal dibuka: " + failList.length + "\n";
        if (failList.length > 0) msg += failList.join("\n");
        showScrollableAlert("Laporan Open PSD (Otomatis)", msg);
    }

    // --- BUTTONS BAWAH ---
    var grpButtons = dlg_list.add("group");
    grpButtons.alignment = "fill";
    grpButtons.spacing = 10;

    btnApplyFilter.onClick = function () {
        var text = txtFilter.text;
        if (!text || !text.replace(/^\s+/, '').replace(/\s+$/, '')) { alert("Paste teks revisi dulu!"); return; }
        var filterResult = buildFilterPlan(text);
        if (!filterResult.plan) {
            setFilterStatus("GAGAL: Tidak ada nomor/section terdeteksi.", [200, 60, 60]);
            alert("Tidak ada nomor (NO/NOMOR/angka) maupun section revisi yang terdeteksi.");
            return;
        }
        unmatchedSections = filterResult.unmatched || null;
        applyPlanToList(filterResult.plan, filterResult.hasSection);
    };

    btnReset.onClick = function () {
        txtFilter.text = "";
        buildTree(allPsdFiles, true);
        btnOpenAll.text = "Buka Semua";
        btnOpenAll.helpTip = "Buka semua file dari semua folder yang tampil.";
        setFilterStatus("Direset. Semua folder dan file tampil kembali.", [80, 80, 80]);
    };
    btnAutoRevisi.helpTip = "Buka SEMUA file di folder-folder yang cocok section (tanpa filter nomor).";
    btnAutoRevisi.onClick = function () {
        var filterText = txtFilter.text;
        if (!filterText || !filterText.replace(/^\s+/, '').replace(/\s+$/, '')) {
            alert("Paste teks revisi dulu di kolom Filter!");
            return;
        }
        var sections = parseRevisiSections(filterText);
        if (sections.length === 0) {
            alert("Tidak ada section yang terdeteksi dari revisi!\nGanti header revisi jadi pola: REVISI <NAMA PROYEK> (contoh: REVISI OB PROFESI, REVISI FREE SEKOLAH).");
            return;
        }
        showSectionSelector(sections);
    };

    // Tombol bawah: Batal + Buka Semua
    var grpRow2 = grpButtons.add("group");
    grpRow2.orientation = "row";
    grpRow2.alignment = ["center", "center"];
    grpRow2.alignChildren = ["center", "center"];
    grpRow2.spacing = 14;

    // Native ScriptUI buttons: rounded by Photoshop/Windows theme.
    // Fixed sizes make both controls look like compact rounded button pills.
    var btnCancel = grpRow2.add("button", undefined, "Batal", { name: "cancel" });
    btnCancel.preferredSize = [105, 28];
    var btnOpenAll = grpRow2.add("button", undefined, "Buka Semua");
    btnOpenAll.preferredSize = [165, 28];
    btnOpenAll.helpTip = "Buka semua file dari semua folder yang tampil.";

    // 0: Cancel, 3: Buka semua file yang tampil
    btnCancel.onClick = function () { dlg_list.close(0); };
    btnOpenAll.onClick = function () { dlg_list.close(3); };

    // Ukuran final setelah semua children terpasang + relayout
    dlg_list.preferredSize = [610, 470];
    try { dlg_list.layout.layout(true); } catch (eL2) { }

    // Show
    var result = dlg_list.show();

    settings.list_x = dlg_list.location.x;
    settings.list_y = dlg_list.location.y;
    saveSettings(settings);

    if (result !== 1 && result !== 3) return;

    var filesToOpen;
    if (result === 1) {
        filesToOpen = getSelectedFiles();
    } else {
        filesToOpen = [];
        for (var g2 = 0; g2 < treeItems.length; g2++) {
            for (var f2 = 0; f2 < treeItems[g2].files.length; f2++) filesToOpen.push(treeItems[g2].files[f2]);
        }
    }

    if (filesToOpen.length == 0) return;

    var successList = [];
    var failList = [];
    for (var i = 0; i < filesToOpen.length; i++) {
        try {
            if (filesToOpen[i].exists) {
                app.open(filesToOpen[i]);
                successList.push(decodeURI(getRelPath(filesToOpen[i], folder)));
            } else {
                failList.push(decodeURI(getRelPath(filesToOpen[i], folder)) + " (File tidak ditemukan)");
            }
        } catch (e) {
            failList.push(decodeURI(getRelPath(filesToOpen[i], folder)) + " (Error: " + e.message + ")");
        }
    }

    var msg = "Berhasil dibuka: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "Gagal dibuka: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");

    showScrollableAlert("Laporan Open PSD", msg);
}

// === HELPERS ===
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

main();
