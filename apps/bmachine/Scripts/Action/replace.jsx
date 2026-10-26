// @target photoshop

// === Settings Persistence ===
var DEFAULT_BASE_INPUT = "\\\\delapanmataair\\Editor 5\\2. REGULER\\#PROJECT SEKOLAH\\2026-2027";
var DEFAULT_BASE_MASTER = "";

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
    if (settingsFile.exists) {
        try {
            settingsFile.open("r");
            var content = settingsFile.read();
            settingsFile.close();
            var data = eval("(" + content + ")");
            if (data.x === undefined) data.x = -1;
            if (data.y === undefined) data.y = -1;
            // Auto-detect on/off (checkbox). Default false = mode compact manual.
            if (data.autoDetect === undefined) data.autoDetect = false;
            if (!("baseInputServer" in data)) data.baseInputServer = DEFAULT_BASE_INPUT;
            if (data.baseInputServer === null) data.baseInputServer = "";
            if (!("baseMasterServer" in data)) data.baseMasterServer = DEFAULT_BASE_MASTER;
            if (data.baseMasterServer === null) data.baseMasterServer = "";
            return data;
        } catch (e) { }
    }
    return {
        x: -1, y: -1,
        autoDetect: false,
        baseInputServer: DEFAULT_BASE_INPUT,
        baseMasterServer: DEFAULT_BASE_MASTER
    };
}

function saveSettings(x, y, autoDetect, baseInputServer, baseMasterServer) {
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
    try {
        var base = (baseInputServer === null || baseInputServer === undefined) ? "" : baseInputServer;
        var escapedBase = base.replace(/\\/g, "\\\\").replace(/"/g, '\\\\"');
        var masterBase = (baseMasterServer === null || baseMasterServer === undefined) ? "" : baseMasterServer;
        var escapedMaster = masterBase.replace(/\\/g, "\\\\").replace(/"/g, '\\\\"');
        settingsFile.open("w");
        settingsFile.write('{"x":' + x + ',"y":' + y + ',"autoDetect":' + (autoDetect ? "true" : "false") + ',"baseInputServer":"' + escapedBase + '","baseMasterServer":"' + escapedMaster + '"}');
        settingsFile.close();
    } catch (e) { }
}

// === Path Helpers & Auto-Detect Logic ===
function normalizePath(p) {
    if (!p) return "";
    var s = decodeURI(p);
    s = s.replace(/^["']+|["']+$/g, "");
    var isUnc = (s.indexOf("\\\\") === 0 || s.indexOf("//") === 0);
    s = s.replace(/\\/g, "/");
    var driveMatch = s.match(/^\/([a-zA-Z])\/(.*)$/);
    if (driveMatch) {
        s = driveMatch[1].toUpperCase() + ":/" + driveMatch[2];
        isUnc = false;
    }
    if (isUnc) {
        s = "//" + s.substring(2).replace(/\/+/g, "/");
    } else {
        s = s.replace(/\/+/g, "/");
    }
    s = s.replace(/\/+$/, "");
    return s;
}

function toWindowsPath(p) {
    if (!p) return "";
    var isUnc = (p.indexOf("//") === 0 || p.indexOf("\\\\") === 0);
    var s = p.replace(/\//g, "\\");
    if (isUnc) {
        s = "\\\\" + s.replace(/^\\+/, "");
    }
    return s;
}

function parseMasterPath(rawPath, baseMasterServer) {
    if (!rawPath) return null;
    var norm = normalizePath(rawPath);
    if (!norm) return null;

    var parts = norm.split("/");
    if (parts.length < 2) return null;

    var targetItem = parts[parts.length - 1];
    var schoolName = parts[parts.length - 2];
    var monthIndex = -1;
    var monthName = "";
    var wilayahName = "";

    var monthRegex = /^\d{2}\s+[A-Za-z]+\s+\d{4}$/;
    for (var i = 0; i < parts.length; i++) {
        if (monthRegex.test(parts[i])) {
            monthIndex = i;
            monthName = parts[i];
            break;
        }
    }

    if (monthIndex !== -1 && monthIndex + 1 < parts.length - 1) {
        wilayahName = parts[monthIndex + 1];
    }

    var monthToSchool = "";
    if (monthIndex !== -1) {
        monthToSchool = parts.slice(monthIndex, parts.length - 1).join("/");
    }

    if (monthIndex === -1 && baseMasterServer) {
        var baseMasterNorm = normalizePath(baseMasterServer);
        var bParts = baseMasterNorm.split("/");
        for (var b = 0; b < bParts.length; b++) {
            if (monthRegex.test(bParts[b])) {
                monthName = bParts[b];
                break;
            }
        }
    }

    return {
        norm: norm,
        targetItem: targetItem,
        schoolName: schoolName,
        monthName: monthName,
        wilayahName: wilayahName,
        monthToSchool: monthToSchool,
        monthIndex: monthIndex
    };
}

function findSchoolInMarketingFolders(monthFolder, schoolName, targetItem) {
    if (!monthFolder || !monthFolder.exists) return null;
    try {
        var marketingFolders = monthFolder.getFiles(function (item) { return item instanceof Folder; });
        var schoolLower = schoolName.toLowerCase();
        var schoolClean = schoolLower.replace(/^\d+[\s._-]+/, "").replace(/^\s+|\s+$/g, "");

        for (var m = 0; m < marketingFolders.length; m++) {
            var mFolder = marketingFolders[m];
            var mPath = decodeURI(mFolder.fullName);

            var candExact = new Folder(mPath + "/" + schoolName);
            if (candExact.exists) {
                var p1 = new Folder(mPath + "/" + schoolName + "/PILIHAN/" + targetItem);
                if (p1.exists) return toWindowsPath(decodeURI(p1.fullName));
                var p2 = new Folder(mPath + "/" + schoolName + "/" + targetItem);
                if (p2.exists) return toWindowsPath(decodeURI(p2.fullName));
                return toWindowsPath(decodeURI(candExact.fullName));
            }

            var schoolSubDirs = mFolder.getFiles(function (item) { return item instanceof Folder; });
            for (var s = 0; s < schoolSubDirs.length; s++) {
                var sDir = schoolSubDirs[s];
                var sName = decodeURI(sDir.name);
                var sLower = sName.toLowerCase();
                var sClean = sLower.replace(/^\d+[\s._-]+/, "").replace(/^\s+|\s+$/g, "");

                if (sLower === schoolLower || (schoolClean.length > 3 && sClean === schoolClean)) {
                    var sPath = decodeURI(sDir.fullName);
                    var p1 = new Folder(sPath + "/PILIHAN/" + targetItem);
                    if (p1.exists) return toWindowsPath(decodeURI(p1.fullName));
                    var p2 = new Folder(sPath + "/" + targetItem);
                    if (p2.exists) return toWindowsPath(decodeURI(p2.fullName));
                    return toWindowsPath(sPath);
                }
            }
        }
    } catch (e) { }
    return null;
}

function autoDetectInputFolder(masterPath, baseServer, baseMaster) {
    var parsed = parseMasterPath(masterPath, baseMaster);
    if (!parsed) {
        return { found: false, path: "", schoolName: "-", targetItem: "-", message: "Format path belum lengkap" };
    }

    var baseNorm = normalizePath(baseServer);

    if (!baseNorm) {
        return {
            found: false,
            path: "",
            schoolName: parsed.schoolName,
            targetItem: parsed.targetItem,
            message: "Mode Manual - silakan Browse folder Input"
        };
    }

    var baseFolder = new Folder(baseNorm);
    var serverReachable = baseFolder.exists;

    if (serverReachable && parsed.monthIndex !== -1 && parsed.monthToSchool !== "") {
        var candPilihan = baseNorm + "/" + parsed.monthToSchool + "/PILIHAN/" + parsed.targetItem;
        var fPilihan = new Folder(candPilihan);
        if (fPilihan.exists) {
            return {
                found: true,
                path: toWindowsPath(candPilihan),
                schoolName: parsed.schoolName,
                targetItem: parsed.targetItem,
                message: "[OK] Terhubung otomatis di folder PILIHAN"
            };
        }

        var parentPilihan = new Folder(baseNorm + "/" + parsed.monthToSchool + "/PILIHAN");
        if (parentPilihan.exists) {
            try {
                var subDirs = parentPilihan.getFiles(function (item) { return item instanceof Folder; });
                var targetLower = parsed.targetItem.toLowerCase();
                var targetStripped = targetLower.replace(/^\d+[\s._-]+/, "").replace(/^\s+|\s+$/g, "");
                for (var k = 0; k < subDirs.length; k++) {
                    var folderName = decodeURI(subDirs[k].name);
                    var fLower = folderName.toLowerCase();
                    var fStripped = fLower.replace(/^\d+[\s._-]+/, "").replace(/^\s+|\s+$/g, "");
                    if (fLower === targetLower || (targetStripped.length > 3 && fStripped === targetStripped)) {
                        return {
                            found: true,
                            path: toWindowsPath(decodeURI(subDirs[k].fsName)),
                            schoolName: parsed.schoolName,
                            targetItem: parsed.targetItem,
                            message: "[OK] Terhubung otomatis di PILIHAN (" + folderName + ")"
                        };
                    }
                }
            } catch (e) { }
        }

        var candDirect = baseNorm + "/" + parsed.monthToSchool + "/" + parsed.targetItem;
        var fDirect = new Folder(candDirect);
        if (fDirect.exists) {
            return {
                found: true,
                path: toWindowsPath(candDirect),
                schoolName: parsed.schoolName,
                targetItem: parsed.targetItem,
                message: "[OK] Terhubung otomatis di folder Sekolah"
            };
        }
    }

    if (serverReachable && parsed.schoolName && parsed.schoolName !== "-") {
        var monthRegex = /^\d{2}\s+[A-Za-z]+\s+\d{4}$/;
        var targetMonthFolder = null;

        var baseFolderName = baseNorm.substring(baseNorm.lastIndexOf("/") + 1);
        if (monthRegex.test(baseFolderName)) {
            targetMonthFolder = baseFolder;
        } else if (parsed.monthName) {
            var candMonth = new Folder(baseNorm + "/" + parsed.monthName);
            if (candMonth.exists) targetMonthFolder = candMonth;
        }

        if (targetMonthFolder && targetMonthFolder.exists) {
            var quickFound = findSchoolInMarketingFolders(targetMonthFolder, parsed.schoolName, parsed.targetItem);
            if (quickFound) {
                return {
                    found: true,
                    path: quickFound,
                    schoolName: parsed.schoolName,
                    targetItem: parsed.targetItem,
                    message: "[OK] Terhubung otomatis (Pencarian Cepat Marketing)"
                };
            }
        } else {
            try {
                var monthDirs = baseFolder.getFiles(function (item) { return item instanceof Folder && monthRegex.test(decodeURI(item.name)); });
                monthDirs.sort(function (a, b) { return decodeURI(b.name) < decodeURI(a.name) ? -1 : 1; });
                for (var md = 0; md < monthDirs.length; md++) {
                    var qf = findSchoolInMarketingFolders(monthDirs[md], parsed.schoolName, parsed.targetItem);
                    if (qf) {
                        return {
                            found: true,
                            path: qf,
                            schoolName: parsed.schoolName,
                            targetItem: parsed.targetItem,
                            message: "[OK] Terhubung otomatis di " + decodeURI(monthDirs[md].name)
                        };
                    }
                }
            } catch (e) { }
        }
    }

    var localPilihan = new Folder(parsed.norm + "/../PILIHAN/" + parsed.targetItem);
    if (localPilihan.exists) {
        return {
            found: true,
            path: toWindowsPath(decodeURI(localPilihan.fsName)),
            schoolName: parsed.schoolName,
            targetItem: parsed.targetItem,
            message: "[OK] Ditemukan di folder lokal PILIHAN"
        };
    }

    var notFoundMsg = serverReachable
        ? "Input belum ditemukan di server (silakan Browse manual)"
        : "Server input tidak terjangkau (silakan Browse manual)";

    return {
        found: false,
        path: "",
        schoolName: parsed.schoolName,
        targetItem: parsed.targetItem,
        message: notFoundMsg
    };
}

function countFilesFast(folderPath, regex) {
    if (!folderPath) return 0;
    var f = new Folder(folderPath);
    if (!f.exists) return 0;
    var total = 0;
    try {
        var items = f.getFiles();
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item instanceof File && item.name.match(regex)) {
                total++;
            } else if (item instanceof Folder) {
                try {
                    var subItems = item.getFiles();
                    for (var j = 0; j < subItems.length; j++) {
                        if (subItems[j] instanceof File && subItems[j].name.match(regex)) {
                            total++;
                        } else if (subItems[j] instanceof Folder) {
                            var sub2 = subItems[j].getFiles();
                            for (var k = 0; k < sub2.length; k++) {
                                if (sub2[k] instanceof File && sub2[k].name.match(regex)) total++;
                            }
                        }
                    }
                } catch (e) { }
            }
        }
    } catch (e) { }
    return total;
}

function writeReplaceErrorResult(message) {
    try {
        var f = new File(Folder.temp + "/bmachine_result.json");
        f.open("w");
        f.encoding = "UTF-8";
        var escaped = message.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
        f.write('{"type":"result","title":"Replace (Auto) Error","lines":["' + escaped + '"]}');
        f.close();
    } catch (e) { }
}

function findLatestContextFile() {
    var tmp = new Folder(Folder.temp);
    var matches = tmp.getFiles("bmachine_context_*.json");
    var best = null;
    if (matches.length > 0) {
        for (var i = 0; i < matches.length; i++) {
            try {
                matches[i].open("r");
                var t = matches[i].lastModified;
                matches[i].close();
                if (best === null || t > best.t) best = { f: matches[i], t: t };
            } catch (e) { }
        }
        if (best !== null) return best.f;
    }
    var legacy = new File(tmp.fsName + "/bmachine_context.json");
    return legacy;
}

function main() {
    // === BMachine Integration (Pre-load context if available) ===
    var bmachineContext = null;
    var tempFile = null;
    var defaultMasterPath = "";
    var defaultInputPath = "";

    // Batch Auto wrapper injects the exact paths for this invocation.
    if ($.global.BMachineBatchContext && $.global.BMachineBatchContext.BatchAutoReplace === true) {
        bmachineContext = $.global.BMachineBatchContext;
    }

    // Prefer the newest bmachine_context_*.json, fall back to fixed name for normal/manual launch.
    if (!bmachineContext) tempFile = findLatestContextFile();
    if (!bmachineContext && tempFile !== null && tempFile.exists) {
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

    // Batch Replace (Auto) memakai path pasangan yang dikirim BMachine; jangan tampilkan dialog.
    // Mendukung 1 banyak pasangan sekaligus (SourceFolders = list) — tiap pasangan di-queue
    // dan dieksekusi berurutan dalam satu invocation, lalu 1 laporan gabungan di akhir.
    if (bmachineContext && bmachineContext.BatchAutoReplace === true) {
        var batchPairs = [];
        var rawSourceFolders = (bmachineContext.SourceFolders && bmachineContext.SourceFolders.length > 0)
            ? bmachineContext.SourceFolders
            : [{ OutputPath: bmachineContext.MasterTemplatePath, SourcePath: "" }];

        for (var p = 0; p < rawSourceFolders.length; p++) {
            var pOut = rawSourceFolders[p].OutputPath || "";
            var pIn = rawSourceFolders[p].SourcePath || "";
            var pOutFolder = new Folder(pOut || "");
            var pInFolder = new Folder(pIn || "");
            if (!pOutFolder.exists || !pInFolder.exists) {
                writeReplaceErrorResult("Replace (Auto): pasangan " + (p + 1) + " tidak lengkap.\n" +
                    "Master/Output: " + pOut + "\nInput/Source: " + pIn + "\n(Salah satu folder tidak ditemukan)");
                return;
            }
            batchPairs.push({ out: pOutFolder, inputFolder: pInFolder });
        }

        if (batchPairs.length === 0) {
            writeReplaceErrorResult("Replace (Auto): tidak ada pasangan Master/Output + Input yang valid.");
            return;
        }

        var previousDisplayDialogs = app.displayDialogs;
        app.displayDialogs = DialogModes.NO;
        try {
            var allResults = [];
            for (var pi = 0; pi < batchPairs.length; pi++) {
                var pairOut = batchPairs[pi].out;
                var pairIn = batchPairs[pi].inputFolder;
                var r = runReplacementLogic(pairOut, pairIn, true, true);
                if (r) allResults.push(r);
                // Sama seperti Replace normal: PSD hasil replace tetap terbuka untuk diedit/save user.
            }

            var combined = showCombinedReport(allResults, "standard");
            // Jika hanya 1 pasangan, tetap tampilkan 1 alert (konsisten dengan behavior lama).
        } catch (batchError) {
            writeReplaceErrorResult("Replace (Auto) gagal: " + batchError.message);
            alert("Replace (Auto) gagal:\n" + batchError.message);
        } finally {
            app.displayDialogs = previousDisplayDialogs;
        }
        return;
    }

    // === UI CONFIG ===
    var settings = loadSettings();
    var currentAutoDetect = settings.autoDetect === true;

    var w = new Window("dialog", "Smart Object Replacer");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 8;
    w.margins = 12;
    w.preferredSize.width = 460;

    // ==========================================
    // PANEL 1: MASTER (.PSD) — compact
    // ==========================================
    var pnlMaster = w.add("panel", undefined, " 1. FOLDER MASTER (.PSD) ");
    pnlMaster.orientation = "column";
    pnlMaster.alignChildren = ["fill", "top"];
    pnlMaster.spacing = 6;
    pnlMaster.margins = 10;

    var grpMasterRow = pnlMaster.add("group");
    grpMasterRow.orientation = "row";
    grpMasterRow.alignChildren = ["fill", "center"];

    var lblMaster = grpMasterRow.add("statictext", undefined, "Master (.psd):");
    lblMaster.preferredSize.width = 80;

    var txtMaster = grpMasterRow.add("edittext", undefined, defaultMasterPath);
    txtMaster.preferredSize.width = 260;
    txtMaster.helpTip = "Drag & Drop folder dari Explorer ke sini, atau Paste (Ctrl+V)";

    var btnClearMaster = grpMasterRow.add("button", undefined, "X");
    btnClearMaster.size = [26, 26];
    btnClearMaster.helpTip = "Hapus text Master";

    var btnBrowseMaster = grpMasterRow.add("button", undefined, "Browse...");
    btnBrowseMaster.preferredSize.width = 75;

    // One-liner info di bawah field Master (Sekolah / Target / jumlah .psd)
    var lblMasterInfo = pnlMaster.add("statictext", undefined, "-");
    lblMasterInfo.preferredSize.width = 420;
    try {
        lblMasterInfo.graphics.foregroundColor = w.graphics.newPen(w.graphics.PenType.SOLID_COLOR, [0.4, 0.4, 0.4, 1], 1);
    } catch (e) { }

    // ==========================================
    // PANEL 2: INPUT FOTO — compact
    // ==========================================
    var pnlInput = w.add("panel", undefined, " 2. FOLDER INPUT FOTO ");
    pnlInput.orientation = "column";
    pnlInput.alignChildren = ["fill", "top"];
    pnlInput.spacing = 6;
    pnlInput.margins = 10;

    var grpInputRow = pnlInput.add("group");
    grpInputRow.orientation = "row";
    grpInputRow.alignChildren = ["fill", "center"];

    var lblInput = grpInputRow.add("statictext", undefined, "Input (Img):");
    lblInput.preferredSize.width = 80;

    var txtInput = grpInputRow.add("edittext", undefined, defaultInputPath);
    txtInput.preferredSize.width = 260;
    txtInput.helpTip = "Folder foto sumber (otomatis jika Mode Otomatis aktif, atau pilih manual via Browse)";

    var btnClearInput = grpInputRow.add("button", undefined, "X");
    btnClearInput.size = [26, 26];
    btnClearInput.helpTip = "Hapus text Input";

    var btnBrowseInput = grpInputRow.add("button", undefined, "Browse...");
    btnBrowseInput.preferredSize.width = 75;

    // One-liner status di bawah field Input
    var lblDetectStatus = pnlInput.add("statictext", undefined, "Menunggu input Master (.psd)...");
    lblDetectStatus.preferredSize.width = 420;
    try {
        lblDetectStatus.graphics.foregroundColor = w.graphics.newPen(w.graphics.PenType.SOLID_COLOR, [0.4, 0.4, 0.4, 1], 1);
    } catch (e) { }

    // Row: Add to Queue (satu-satunya tombol tambah, di luar panel agar konsisten posisi)
    var grpInputActions = pnlInput.add("group");
    grpInputActions.orientation = "row";
    grpInputActions.alignChildren = ["fill", "center"];
    var grpSpacer = grpInputActions.add("group");
    grpSpacer.alignment = ["fill", "fill"];
    var btnAddQueue = grpInputActions.add("button", undefined, "+ Tambah ke Antrian");
    btnAddQueue.preferredSize.width = 160;
    btnAddQueue.preferredSize.height = 26;
    btnAddQueue.helpTip = "Tambahkan pasangan Master dan Input ini ke Antrian (Queue)";

    // ==========================================
    // PANEL 3: MODE (checkbox + tombol Pengaturan Base)
    // ==========================================
    var grpMode = w.add("group");
    grpMode.orientation = "row";
    grpMode.alignChildren = ["fill", "center"];

    var chkAutoDetect = grpMode.add("checkbox", undefined, "Mode Otomatis (auto-detect Input via Base)");
    chkAutoDetect.value = currentAutoDetect;
    chkAutoDetect.helpTip = "Kalau aktif: Input diisi otomatis dari Base Server saat Master di-paste. Kalau mati: Input wajib Browse manual.";

    var btnBaseSettings = grpMode.add("button", undefined, "Pengaturan...");
    btnBaseSettings.preferredSize.width = 100;
    btnBaseSettings.helpTip = "Atur Base Server & Base Master di window baru.";

    // --- Dialog Pengaturan Base (window baru) ---
    function openBaseSettingsDialog() {
        var d = new Window("dialog", "Pengaturan Base Server & Base Master");
        d.orientation = "column";
        d.alignChildren = ["fill", "top"];
        d.spacing = 10;
        d.margins = 14;
        d.preferredSize.width = 520;

        var lblDesc = d.add("statictext", undefined,
            "Base Server : root folder input di server (auto-detect saat Mode Otomatis).\n" +
            "Base Master : (opsional) folder master lokal bila format path berbeda.",
            { multiline: true });
        lblDesc.preferredSize.width = 480;
        try {
            lblDesc.graphics.foregroundColor = d.graphics.newPen(d.graphics.PenType.SOLID_COLOR, [0.4, 0.4, 0.4, 1], 1);
        } catch (e) { }

        var grpSrv = d.add("group");
        grpSrv.orientation = "row";
        grpSrv.alignChildren = ["fill", "center"];
        var lblSrv = grpSrv.add("statictext", undefined, "Base Server:");
        lblSrv.preferredSize.width = 90;
        var txtSrv = grpSrv.add("edittext", undefined, settings.baseInputServer || "");
        txtSrv.preferredSize.width = 360;
        var btnSrvBrowse = grpSrv.add("button", undefined, "Browse...");
        btnSrvBrowse.preferredSize.width = 75;
        btnSrvBrowse.onClick = function () {
            var f = Folder.selectDialog("Pilih Base Server Input");
            if (f) txtSrv.text = decodeURI(f.fsName || f.fullName);
        };

        var grpMst = d.add("group");
        grpMst.orientation = "row";
        grpMst.alignChildren = ["fill", "center"];
        var lblMst = grpMst.add("statictext", undefined, "Base Master:");
        lblMst.preferredSize.width = 90;
        var txtMst = grpMst.add("edittext", undefined, settings.baseMasterServer || "");
        txtMst.preferredSize.width = 360;
        txtMst.helpTip = "Opsional. Kosongkan bila format path Master sama dengan server.";
        var btnMstBrowse = grpMst.add("button", undefined, "Browse...");
        btnMstBrowse.preferredSize.width = 75;
        btnMstBrowse.onClick = function () {
            var f = Folder.selectDialog("Pilih Base Folder Master lokal");
            if (f) txtMst.text = decodeURI(f.fsName || f.fullName);
        };

        var grpBtn = d.add("group");
        grpBtn.alignment = "right";
        grpBtn.spacing = 8;
        var btnSrvClear = grpBtn.add("button", undefined, "Kosongkan");
        btnSrvClear.onClick = function () { txtSrv.text = ""; txtSrv.active = true; };
        var btnOk = grpBtn.add("button", undefined, "Simpan", { name: "ok" });
        var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });
        btnOk.onClick = function () { d.close(1); };
        btnCancel.onClick = function () { d.close(0); };

        d.center();
        if (d.show() === 1) {
            var srv = txtSrv.text.replace(/^["']+|["']+$/g, "").replace(/[\\\/]+$/, "").replace(/^\s+|\s+$/g, "");
            var mst = txtMst.text.replace(/^["']+|["']+$/g, "").replace(/[\\\/]+$/, "").replace(/^\s+|\s+$/g, "");
            settings.baseInputServer = srv;
            settings.baseMasterServer = mst;
            saveSettings(w.location.x, w.location.y, chkAutoDetect.value === true, srv, mst);
            triggerMasterRefresh();
        }
    }

    btnBaseSettings.onClick = function () {
        openBaseSettingsDialog();
    };

    chkAutoDetect.onChange = function () {
        saveSettings(w.location.x, w.location.y, chkAutoDetect.value === true,
            settings.baseInputServer || "", settings.baseMasterServer || "");
        triggerMasterRefresh();
    };


    // ==========================================
    // LOGIKA UPDATE INFO REALTIME
    // ==========================================
    function updateMasterInfoOnly(raw) {
        if (!raw || raw.replace(/\s+/g, "") === "") {
            lblMasterInfo.text = "-";
            return;
        }

        // Baca Base Master dari settings (diupdate oleh dialog Pengaturan).
        var parsed = parseMasterPath(raw, settings.baseMasterServer || "");
        var info = "-";
        if (parsed) {
            info = "Sekolah: " + (parsed.schoolName || "-") + "  |  Target: " + (parsed.targetItem || "-");
        }
        var mCount = countFilesFast(raw, /\.(psd|psb)$/i);
        if (mCount > 0) {
            info += "  |  " + mCount + " file .psd";
        } else if (new Folder(raw).exists) {
            info += "  |  0 file .psd";
        } else {
            info += "  |  folder tidak ada";
        }
        lblMasterInfo.text = info;
    }

    function triggerMasterRefresh() {
        var raw = txtMaster.text;
        if (!raw || raw.replace(/\s+/g, "") === "") {
            lblMasterInfo.text = "-";
            lblDetectStatus.text = "Menunggu input Master (.psd)...";
            return;
        }

        var normalized = toWindowsPath(normalizePath(raw));
        if (normalized && normalized !== raw && raw.indexOf("\\") !== -1) {
            txtMaster.text = normalized;
            raw = normalized;
        }

        updateMasterInfoOnly(raw);

        // Mode Otomatis aktif => auto-detect Input dari Base (nilai settings, diupdate dialog Pengaturan).
        if (chkAutoDetect.value) {
            var res = autoDetectInputFolder(raw, settings.baseInputServer || "", settings.baseMasterServer || "");
            lblDetectStatus.text = res.message;
            if (res.found && res.path) {
                txtInput.text = res.path;
            }
            return;
        }

        // Mode Manual: coba sibling lokal bila Input kosong.
        if (!txtInput.text || txtInput.text.replace(/\s+/g, "") === "") {
            var parsedLocal = parseMasterPath(raw, "");
            if (parsedLocal) {
                var cand = new Folder(parsedLocal.norm + "/../PILIHAN/" + parsedLocal.targetItem);
                if (cand.exists) {
                    txtInput.text = toWindowsPath(decodeURI(cand.fsName));
                    lblDetectStatus.text = "[OK] Input ditemukan di folder lokal PILIHAN (sibling Master)";
                    return;
                }
            }
            lblDetectStatus.text = "Mode Manual - silakan Browse folder Input";
        } else {
            var iCount = countFilesFast(txtInput.text, /\.(png|jpe?g|psd)$/i);
            if (iCount > 0) {
                lblDetectStatus.text = iCount + " file gambar di Input";
            } else {
                lblDetectStatus.text = "Input: " + new File(txtInput.text).displayName;
            }
        }
    }

    // Event Handlers (Instant OnChanging saat Paste/Drop)
    txtMaster.onChanging = function () {
        triggerMasterRefresh();
    };

    txtMaster.onChange = function () {
        triggerMasterRefresh();
    };

    txtInput.onChanging = function () {
        if (chkAutoDetect.value) return; // auto-detect sedang pegang, jangan override
        var iCount = countFilesFast(txtInput.text, /\.(png|jpe?g|psd)$/i);
        if (iCount > 0) {
            lblDetectStatus.text = iCount + " file gambar di Input";
        } else {
            lblDetectStatus.text = "Input: " + new File(txtInput.text).displayName;
        }
    };

    txtInput.onChange = function () {
        txtInput.onChanging();
    };

    btnBrowseMaster.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Master");
        if (f) {
            txtMaster.text = decodeURI(f.fsName || f.fullName);
            triggerMasterRefresh();
        }
    };

    btnClearMaster.onClick = function () {
        txtMaster.text = "";
        triggerMasterRefresh();
        txtMaster.active = true;
    };

    btnBrowseInput.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Seleksi");
        if (f) {
            txtInput.text = decodeURI(f.fsName || f.fullName);
            txtInput.onChanging();
        }
    };

    btnClearInput.onClick = function () {
        txtInput.text = "";
        triggerMasterRefresh();
        txtInput.active = true;
    };

    // Refresh saat pertama kali dialog terbuka jika Master sudah terisi
    if (txtMaster.text != "") {
        triggerMasterRefresh();
    }

    // ==========================================
    // TOMBOL AKSI UTAMA
    // ==========================================
    var grpBtn = w.add("group");
    grpBtn.alignment = "center";
    grpBtn.spacing = 12;

    var btnRun = grpBtn.add("button", undefined, "REPLACE", { name: "ok" });
    btnRun.preferredSize = [130, 32];
    var btnRevisi = grpBtn.add("button", undefined, "REPLACE REVISI");
    btnRevisi.preferredSize = [140, 32];
    var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });
    btnCancel.preferredSize = [80, 32];

    // Logic Add Queue
    // Panel antrian TIDAK dibuat di awal supaya tidak ada ruang kosong.
    // Panel baru dibuat saat item pertama ditambahkan, lalu jendela tumbuh.
    var queueData = []; // {master, input}
    var selectedRevisiMode = "exact"; // "exact" (Persis) | "flex" (Fleksibel)
    var grpQueue = null;
    var listQueue = null;
    var btnClearQueue = null;

    function queueListHeight() {
        // Tinggi daftar mengikuti jumlah item, jadi jendela tumbuh bertahap.
        var h = queueData.length * 22 + 12;
        if (h < 46) h = 46;
        if (h > 220) h = 220;
        return h;
    }

    function refreshQueueLayout() {
        w.layout.layout(true);
        w.layout.resize();
        w.update();
    }

    function ensureQueuePanel() {
        if (grpQueue) return;

        grpQueue = w.add("panel", undefined, " 3. DAFTAR ANTRIAN KERJA ");
        grpQueue.orientation = "column";
        grpQueue.alignChildren = ["fill", "top"];
        grpQueue.spacing = 6;
        grpQueue.margins = 10;

        grpQueue.add("statictext", undefined, "Daftar Antrian (Queue):");
        listQueue = grpQueue.add("listbox", undefined, [], { multiselect: true });
        listQueue.preferredSize.width = 500;
        listQueue.preferredSize.height = queueListHeight();

        var grpQueueControl = grpQueue.add("group");
        grpQueueControl.orientation = "row";
        grpQueueControl.alignChildren = ["left", "center"];
        btnClearQueue = grpQueueControl.add("button", undefined, "Hapus Terpilih");
        btnClearQueue.size = [110, 25];
        btnClearQueue.enabled = false;

        listQueue.onChange = function () {
            btnClearQueue.enabled = (listQueue.selection != null);
        };

        btnClearQueue.onClick = function () {
            if (!listQueue.selection) return;
            var limits = listQueue.selection;
            var indices = [];
            for (var i = 0; i < limits.length; i++) indices.push(limits[i].index);
            indices.sort(function (a, b) { return b - a; }); // Descending

            for (var j = 0; j < indices.length; j++) {
                listQueue.remove(indices[j]);
                queueData.splice(indices[j], 1);
            }

            if (queueData.length == 0) {
                // Hapus panel dari layout supaya tidak menyisakan ruang kosong.
                w.remove(grpQueue);
                grpQueue = null;
                listQueue = null;
                btnClearQueue = null;
            } else {
                grpQueue.text = " 3. DAFTAR ANTRIAN KERJA (" + queueData.length + ") ";
                listQueue.preferredSize.height = queueListHeight();
            }
            refreshQueueLayout();
        };
    }

    btnAddQueue.onClick = function () {
        if (txtMaster.text != "" && txtInput.text == "") {
            triggerMasterRefresh();
        }

        if (txtMaster.text == "" || txtInput.text == "") {
            alert("Isi Folder Master dan Input dulu!");
            return;
        }

        // Add to data
        queueData.push({
            master: txtMaster.text,
            input: txtInput.text
        });

        // Buat panel antrian saat item pertama masuk
        ensureQueuePanel();

        // Add to UI
        var label = "M: " + new File(txtMaster.text).displayName + " | I: " + new File(txtInput.text).displayName;
        listQueue.add("item", label);
        listQueue.selection = listQueue.items[listQueue.items.length - 1];

        grpQueue.text = " 3. DAFTAR ANTRIAN KERJA (" + queueData.length + ") ";
        grpQueue.visible = true;
        listQueue.preferredSize.height = queueListHeight();
        refreshQueueLayout();

        // Clear fields for next entry
        txtInput.text = "";
        txtMaster.text = "";
        triggerMasterRefresh();
    };

    // === EXECUTION LOGIC ===
    btnRun.onClick = function () {
        w.close(1); // Standard Run
    };

    btnRevisi.onClick = function () {
        // Tanya mode dulu (Persis / Fleksibel) sebelum lanjut
        var mode = askRevisiMode();
        if (mode === null) return; // Batal -> tetap di dialog utama
        selectedRevisiMode = mode;
        w.close(2); // Revisi Run
    };

    btnCancel.onClick = function () {
        w.close(0);
    };

    // Restore window position
    if (settings.x > 0 && settings.y > 0) {
        w.location = [settings.x, settings.y];
    } else {
        w.center();
    }

    txtMaster.active = true;

    var result = w.show();

    // Simpan posisi terakhir APA PUN hasilnya (termasuk Cancel/tutup),
    // supaya dialog selalu ingat tempat terakhir kali dipakai.
    // Mode + Base kini persist hanya dari dialog Pengaturan & checkbox.
    saveSettings(
        w.location.x,
        w.location.y,
        chkAutoDetect.value === true,
        settings.baseInputServer || "",
        settings.baseMasterServer || ""
    );

    if (result != 1 && result != 2) return; // Cancel

    // --- COLLECT JOBS ---
    var jobsToRun = [];

    // 1. Add Queue items
    for (var i = 0; i < queueData.length; i++) {
        jobsToRun.push(queueData[i]);
    }

    // 2. Add Current Fields (if valid and not empty)
    // NOTE: Only add if fields are filled. If Queue has items but fields empty, ignore fields.
    // If Queue empty, fields MUST be filled.
    if (txtMaster.text != "" && txtInput.text == "") {
        triggerMasterRefresh();
    }

    if (txtMaster.text != "" && txtInput.text != "") {
        // Optional: Check duplication?
        jobsToRun.push({ master: txtMaster.text, input: txtInput.text });
    }

    if (jobsToRun.length == 0) {
        alert("Tidak ada Job yang valid (Master & Input kosong)!");
        return;
    }

    // --- EXECUTE JOBS ---
    var totalSuccess = 0;
    var totalFail = 0;
    var totalReplaced = 0; // For revisi

    // Jika lebih dari 1 antrian (Queue Mode) -> tahan semua alert,
    // tampilkan SATU alert gabungan setelah semua job benar-benar selesai.
    var isQueueMode = (jobsToRun.length > 1);
    var collectedResults = [];

    for (var j = 0; j < jobsToRun.length; j++) {
        var job = jobsToRun[j];
        var mFolder = new Folder(job.master);
        var iFolder = new Folder(job.input);

        if (!mFolder.exists || !iFolder.exists) {
            collectedResults.push({
                type: "missing",
                masterName: decodeURI(mFolder.name || job.master),
                success: [],
                fail: ["Folder tidak ditemukan (Master: " + (mFolder.exists ? "OK" : "MISSING") + ", Input: " + (iFolder.exists ? "OK" : "MISSING") + ")"],
                skipped: true,
                skipMsg: "Folder Master atau Input tidak ditemukan."
            });
            continue;
        }

        var res;
        if (result == 1) {
            // STANDARD REPLACE
            res = runReplacementLogic(mFolder, iFolder, isQueueMode);
        } else {
            // REVISI REPLACE
            res = runRevisiLogic(mFolder, iFolder, selectedRevisiMode, isQueueMode);
        }
        if (res) collectedResults.push(res);
    }

    // Tampilkan SATU laporan gabungan (mode antrian) dengan 2 pemberitahuan:
    // (1) Ringkasan total, (2) Rincian per-antrian.
    if (isQueueMode && collectedResults.length > 0) {
        showCombinedReport(collectedResults, (result == 1) ? "standard" : "revisi");
    }
}

// Global invocation
main();

// ==========================================
// Core Logic (Helper Functions)
// ==========================================
// suppressAlert = true -> tidak menampilkan alert per-job (dipakai mode antrian),
// melainkan mengembalikan objek hasil untuk digabung di satu alert akhir.
function runReplacementLogic(templateFolder, inputFolder, suppressAlert, autoBatch) {

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
        var skipMsg = "Job Skipped (No files).\nMaster: " + templateCount + "\nInput: " + inputCount;
        if (autoBatch) writeReplaceErrorResult(skipMsg);
        if (!suppressAlert) alert(skipMsg);
        return { type: "standard", masterName: decodeURI(templateFolder.name), success: [], fail: [], skipped: true, skipMsg: skipMsg };
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
                // Hapus spasi di awal/akhir dan suffix " (1)" atau spasi banyak "    (2)"
                var inputBaseNameStripped = inputBaseName.replace(/^\s+|\s+$/g, "").replace(/\s*\(\d+\)$/, "");
                
                // Coba cocokan kalau format master hanya angka misal "1" dan input " (1)"
                var inputJustNumber = "";
                var numberMatch = inputBaseNameStripped.match(/^(\d+)$/) || inputBaseName.match(/^\s*\((\d+)\)\s*$/) || inputBaseName.match(/^(\d+)\s*\(\d+\)$/);
                if (numberMatch) inputJustNumber = numberMatch[1];

                if (inputBaseName === templateBaseName || inputBaseNameStripped === templateBaseName || (inputJustNumber !== "" && inputJustNumber === templateBaseName)) {
                    var inputRelDir = decodeURI(input.parent.fullName).replace(decodeURI(inputFolder.fullName), "");
                    if (inputRelDir.indexOf("/") == 0) inputRelDir = inputRelDir.substring(1);

                    // Template relDir bisa berupa "PAUD" sementara inputRelDir "PAUD/BRIMOB"
                    // Cocok jika sama persis ATAU inputRelDir adalah turunan dari templateRelDir
                    var isSameOrChildFolder = false;
                    if (templateRelDir === "") {
                        // Jika master ada di root, hanya cocok jika input juga di root
                        isSameOrChildFolder = (inputRelDir === "");
                    } else {
                        isSameOrChildFolder = (inputRelDir === templateRelDir || inputRelDir.indexOf(templateRelDir + "/") === 0);
                    }

                    if (isSameOrChildFolder) {
                        matchedByNameInSubfolder.push(input);
                    } else {
                        matchedByNameAnywhere.push(input);
                    }
                }
            }
            if (matchedByNameInSubfolder.length > 0) {
                matchedInputs = matchedByNameInSubfolder;
            } else if (templateRelDir === "" && matchedByNameAnywhere.length > 0) {
                // Hanya izinkan fallback ke 'anywhere' jika template sendiri berada di root folder
                matchedInputs = matchedByNameAnywhere;
            }
        }

        // 2. By Number (if Name failed)
        if (matchedInputs.length === 0) {
            // Ambil nomor utama di awal nama file template (sebelum kurung jika ada)
            var templateNumberMatch = template.name.match(/^(\d+)/) || template.name.match(/(\d+)/);
            if (templateNumberMatch) {
                var templateNumber = templateNumberMatch[1];
                var matchedByNumInSubfolder = [];
                var matchedByNumAnywhere = [];
                for (var j = 0; j < inputFiles.length; j++) {
                    var input = inputFiles[j];
                    // Ambil nomor utama input (misal "1 (1)" -> nomor utamanya adalah 1, BUKAN angka di dalam kurung)
                    var inputBaseNumberMatch = input.name.match(/^(\d+)/);
                    var inputNumber = inputBaseNumberMatch ? inputBaseNumberMatch[1] : null;

                    if (inputNumber !== null && inputNumber === templateNumber) {
                        var inputRelDir = decodeURI(input.parent.fullName).replace(decodeURI(inputFolder.fullName), "");
                        if (inputRelDir.indexOf("/") == 0) inputRelDir = inputRelDir.substring(1);

                        var isSameOrChildFolder = false;
                        if (templateRelDir === "") {
                            isSameOrChildFolder = (inputRelDir === "");
                        } else {
                            isSameOrChildFolder = (inputRelDir === templateRelDir || inputRelDir.indexOf(templateRelDir + "/") === 0);
                        }

                        if (isSameOrChildFolder) {
                            matchedByNumInSubfolder.push(input);
                        } else {
                            matchedByNumAnywhere.push(input);
                        }
                    }
                }
                if (matchedByNumInSubfolder.length > 0) {
                    matchedInputs = matchedByNumInSubfolder;
                } else if (templateRelDir === "" && matchedByNumAnywhere.length > 0) {
                    matchedInputs = matchedByNumAnywhere;
                }
            }
        }

        // Sort matches: Utamakan pengelompokan folder yang sama, lalu urutan nama file
        matchedInputs.sort(function (a, b) {
            var aDir = decodeURI(a.parent.fullName);
            var bDir = decodeURI(b.parent.fullName);
            if (aDir !== bDir) {
                return aDir < bDir ? -1 : 1;
            }

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

        // Kelompokkan file input per parent folder (agar tidak pernah mencampur file dari 2 folder berbeda)
        var inputsByFolder = {};
        for (var mi = 0; mi < matchedInputs.length; mi++) {
            var parentKey = decodeURI(matchedInputs[mi].parent.fullName);
            if (!inputsByFolder[parentKey]) inputsByFolder[parentKey] = [];
            inputsByFolder[parentKey].push(matchedInputs[mi]);
        }

        // Ambil kelompok folder pertama yang cocok
        var targetGroup = [];
        for (var pKey in inputsByFolder) {
            if (inputsByFolder.hasOwnProperty(pKey)) {
                targetGroup = inputsByFolder[pKey];
                break;
            }
        }

        // Cari file spesifik untuk XL dan S di dalam grup folder tersebut
        var fileForXL = null;
        var fileForS = null;

        if (targetGroup.length === 1) {
            // Jika HANYA ada 1 input file (misal "1(2).png" saja tanpa pasangan 1(1)/1(3)),
            // maka file tersebut hanya menggantikan XL saja, dan S TIDAK diganti.
            fileForXL = targetGroup[0];
            fileForS = null;
        } else if (targetGroup.length >= 2) {
            // Urutkan grup: nomor kurung lebih kecil duluan (misal 1(1) sebelum 1(2), 1(2) sebelum 1(3))
            targetGroup.sort(function (a, b) {
                var aNum = 0, bNum = 0;
                var aM = a.displayName.match(/\((\d+)\)/);
                var bM = b.displayName.match(/\((\d+)\)/);
                if (aM) aNum = parseInt(aM[1], 10);
                if (bM) bNum = parseInt(bM[1], 10);
                if (aNum !== bNum) return aNum - bNum;
                return a.name.toLowerCase() < b.name.toLowerCase() ? -1 : 1;
            });

            // Cek apakah ada file yang eksplisit (1) dan (2)
            var explicit1 = null;
            var explicit2 = null;
            for (var gi = 0; gi < targetGroup.length; gi++) {
                var gItem = targetGroup[gi];
                var gName = gItem.displayName;
                if (/\(\s*1\s*\)/.test(gName) && !explicit1) {
                    explicit1 = gItem;
                } else if (/\(\s*2\s*\)/.test(gName) && !explicit2) {
                    explicit2 = gItem;
                }
            }

            if (explicit1 && explicit2) {
                fileForXL = explicit1;
                fileForS = explicit2;
            } else if (explicit1) {
                fileForXL = explicit1;
                // Ambil file lain untuk S
                for (var gi2 = 0; gi2 < targetGroup.length; gi2++) {
                    if (targetGroup[gi2] !== fileForXL) {
                        fileForS = targetGroup[gi2];
                        break;
                    }
                }
            } else {
                // Tidak ada (1), misal pasangan 1(2) & 1(3), atau foto tanpa tanda kurung
                // Ambil file urutan pertama untuk XL, file berikutnya untuk S
                fileForXL = targetGroup[0];
                fileForS = targetGroup[1];
            }

            // Proteksi: file XL dan S tidak boleh sama
            if (fileForS === fileForXL) {
                fileForS = null;
            }
        }

        try {
            var doc = app.open(template);
            var smartXL = findSmartObject(doc, "XL");
            var smartS = findSmartObject(doc, "S");

            if (!smartXL) {
                failList.push(relPath(templateFolder, template) + " (Layer XL missing)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }

            if (fileForXL) {
                // Ganti XL
                doc.activeLayer = smartXL;
                replaceSmartContent(fileForXL);
                smartXL.name = fileForXL.displayName.replace(/\.[^\.]+$/, "");

                // Ganti S jika smartS ada dan file (2) / file kedua ada di folder yang sama
                if (smartS && fileForS) {
                    doc.activeLayer = smartS;
                    replaceSmartContent(fileForS);
                    smartS.name = fileForS.displayName.replace(/\.[^\.]+$/, "");
                }

                // Sama dengan REPLACE manual: biarkan dokumen hasil terbuka di Photoshop.
                // Auto hanya menghilangkan pemilihan path; tidak mengubah siklus dokumen.
                successList.push(relPath(templateFolder, template));
            } else {
                failList.push(relPath(templateFolder, template) + " (No input match)");
                doc.close(SaveOptions.DONOTSAVECHANGES);
                continue;
            }
        } catch (e) {
            try {
                if (autoBatch && app.documents.length > 0) app.activeDocument.close(SaveOptions.DONOTSAVECHANGES);
            } catch (closeError) { }
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

    // Send Result to BMachine; queue mode gabung laporan setelah semua job selesai.
    if (!suppressAlert || autoBatch) {
        var f = new File(Folder.temp + "/bmachine_result.json");
        f.open("w");
        f.encoding = "UTF-8";
        var escaped = [];
        for (var i = 0; i < report.length; i++) {
            escaped.push('"' + report[i].replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"');
        }
        f.write('{"type":"result","title":"Replacer Summary","lines":[' + escaped.join(',') + ']}');
        f.close();
    }

    var msg = "Master: " + decodeURI(templateFolder.name) + "\n\n";
    msg += "Berhasil: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    if (!suppressAlert) showScrollableAlert("Laporan Replace (Standard)", msg);

    return {
        type: "standard",
        masterName: decodeURI(templateFolder.name),
        success: successList,
        fail: failList,
        skipped: false
    };
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
// Match Smart Object name with input files, prioritizing same relative folder
// ==========================================
function runRevisiLogic(masterFolder, inputFolder, mode, suppressAlert) {
    mode = (mode === "flex") ? "flex" : "exact"; // default: exact (Persis)

    // Scan for PSD/PSB files in master folder
    var masterFiles = scanFolderForFiles(masterFolder, /\.(psd|psb)$/i);
    masterFiles.sort(sortByNumberInFilename);

    // Scan for replacement files in input folder
    var inputFiles = scanFolderForFiles(inputFolder, /\.(png|psd|jpe?g)$/i);

    var modeLabel = (mode === "exact") ? "Persis (Exact)" : "Fleksibel (By Number)";

    if (masterFiles.length == 0) {
        var skipMsgM = "Job Skipped (No PSD/PSB in Master)!";
        if (!suppressAlert) alert(skipMsgM);
        return { type: "revisi", modeLabel: modeLabel, masterName: decodeURI(masterFolder.name), success: [], fail: [], replacedCount: 0, totalSmartObjects: 0, skipped: true, skipMsg: skipMsgM };
    }

    if (inputFiles.length == 0) {
        var skipMsgI = "Job Skipped (No Image in Input)!";
        if (!suppressAlert) alert(skipMsgI);
        return { type: "revisi", modeLabel: modeLabel, masterName: decodeURI(masterFolder.name), success: [], fail: [], replacedCount: 0, totalSmartObjects: 0, skipped: true, skipMsg: skipMsgI };
    }

    // Helper to get relative directory path
    function getRelDir(file, root) {
        var rel = decodeURI(file.parent.fullName).replace(decodeURI(root.fullName), "");
        if (rel.indexOf("/") == 0) rel = rel.substring(1);
        return rel;
    }

    // Build lookup maps untuk input:
    //  - inputByExactKey : "nomor:sisa-nama" -> [files]      (Mode Persis)
    //  - inputByNumber   : "nomor"           -> [files]      (Mode Fleksibel & fallback)
    var inputByExactKey = {};
    var inputByNumber = {};

    function pushToMap(map, key, file) {
        if (key === null || key === undefined || key === "") return;
        if (!map[key]) map[key] = [];
        // hindari duplikat file yang sama
        for (var q = 0; q < map[key].length; q++) {
            if (map[key][q] === file) return;
        }
        map[key].push(file);
    }

    for (var i = 0; i < inputFiles.length; i++) {
        var file = inputFiles[i];
        var num = extractLeadingNumber(file.displayName);
        var exactKey = buildExactKey(file.displayName);

        pushToMap(inputByNumber, num, file);
        if (mode === "exact") {
            pushToMap(inputByExactKey, exactKey, file);
        }
    }

    var successList = [];
    var failList = [];
    var totalSmartObjects = 0;
    var replacedCount = 0;

    // Process each master file
    for (var m = 0; m < masterFiles.length; m++) {
        var masterFile = masterFiles[m];
        var masterRelDir = getRelDir(masterFile, masterFolder);

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
                var smartNameRaw = smartObj.name;

                var smartNum = extractLeadingNumber(smartNameRaw);
                var smartExactKey = buildExactKey(smartNameRaw);

                var candidates = null;

                if (mode === "exact") {
                    // Mode PERSIS: dahulukan nama persis (nomor + sisa nama)
                    if (inputByExactKey[smartExactKey] && inputByExactKey[smartExactKey].length > 0) {
                        candidates = inputByExactKey[smartExactKey].slice(0);
                    } else if (smartNum !== "" && inputByNumber[smartNum] && inputByNumber[smartNum].length > 0) {
                        // Fallback: cocok berdasarkan nomor saja
                        candidates = inputByNumber[smartNum].slice(0);
                    }
                } else {
                    // Mode FLEKSIBEL: hanya nomor
                    if (smartNum !== "" && inputByNumber[smartNum] && inputByNumber[smartNum].length > 0) {
                        candidates = inputByNumber[smartNum].slice(0);
                    }
                }

                if (candidates && candidates.length > 0) {
                    // Sort candidates to find best match
                    // Priority 1: Same relative directory
                    // Priority 2: File type (PNG > PSD > JPG)
                    candidates.sort(function (a, b) {
                        var aRel = getRelDir(a, inputFolder);
                        var bRel = getRelDir(b, inputFolder);

                        var aMatch = (aRel === masterRelDir || aRel.indexOf(masterRelDir + "/") === 0);
                        var bMatch = (bRel === masterRelDir || bRel.indexOf(masterRelDir + "/") === 0);

                        if (aMatch && !bMatch) return -1;
                        if (!aMatch && bMatch) return 1;

                        // Jika sama-sama match di turunan folder, utamakan yang satu subfolder terdalam
                        var aDir = decodeURI(a.parent.fullName);
                        var bDir = decodeURI(b.parent.fullName);
                        if (aDir !== bDir) {
                            return aDir < bDir ? -1 : 1;
                        }

                        // Tie-break with extension priority
                        var aExt = a.name.match(/\.([^\.]+)$/i)[1].toLowerCase();
                        var bExt = b.name.match(/\.([^\.]+)$/i)[1].toLowerCase();
                        var priority = { "png": 3, "psd": 2, "jpg": 1, "jpeg": 1 };

                        return (priority[bExt] || 0) - (priority[aExt] || 0);
                    });

                    var bestMatch = candidates[0];

                    try {
                        doc.activeLayer = smartObj;
                        replaceSmartContent(bestMatch);
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
    report.push("Mode: " + modeLabel);
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

    var msg = "Master: " + decodeURI(masterFolder.name) + "\n";
    msg += "Mode: " + modeLabel + "\n";
    msg += "Smart Object Replaced: " + replacedCount + "\n\n";
    msg += "File Berhasil: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "File Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    if (!suppressAlert) showScrollableAlert("Laporan Replace (Revisi)", msg);

    return {
        type: "revisi",
        modeLabel: modeLabel,
        masterName: decodeURI(masterFolder.name),
        success: successList,
        fail: failList,
        replacedCount: replacedCount,
        totalSmartObjects: totalSmartObjects,
        skipped: false
    };
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

// ==========================================
// Name / Number Normalization Helpers (untuk Replace Revisi)
// ==========================================
// Cari nomor utama dari sebuah nama (tanpa ekstensi). Logika:
//  - "(21). MUHAMMAD NIZAM RAMDHAN"  -> "21"  (kurung di AWAL => ambil isi kurung)
//  - "21(1). MUHAMMAD NIZAM RAMDHAN" -> "21"  (angka luar, isi kurung diabaikan)
//  - "21. MUHAMMAD NIZAM RAMDHAN"    -> "21"
//  - "21"                            -> "21"
function extractLeadingNumber(rawName) {
    if (!rawName) return "";
    var name = decodeURI(String(rawName));
    name = name.replace(/\.[^\.]+$/, ""); // buang ekstensi
    name = name.replace(/^\s+|\s+$/g, ""); // trim

    // 1) Kurung di AWAL, misal "(21). NAMA" atau "(21) NAMA" atau "(21)"
    var mParen = name.match(/^\(\s*(\d+)\s*\)/);
    if (mParen) return mParen[1];

    // 2) Angka di awal, misal "21. NAMA" / "21(1). NAMA" / "21 NAMA" / "21"
    var mNum = name.match(/^(\d+)/);
    if (mNum) return mNum[1];

    // 3) Fallback: angka pertama yang ditemukan
    var mAny = name.match(/(\d+)/);
    return mAny ? mAny[1] : "";
}

// Buang prefiks nomor + pemisah, dan isi kurung di awal, untuk perbandingan nama.
// Contoh:
//  "(21). MUHAMMAD NIZAM RAMDHAN"  -> "muhammad nizam ramdhan"
//  "21(1). MUHAMMAD NIZAM RAMDHAN" -> "muhammad nizam ramdhan"
//  "21. MUHAMMAD NIZAM RAMDHAN"    -> "muhammad nizam ramdhan"
//  "21"                            -> ""
function stripLeadingNumber(rawName) {
    if (!rawName) return "";
    var name = decodeURI(String(rawName));
    name = name.replace(/\.[^\.]+$/, ""); // buang ekstensi
    name = name.replace(/^\s+|\s+$/g, ""); // trim

    // Kurung di awal
    name = name.replace(/^\(\s*\d+\s*\)\s*[\.\-_]?\s*/, "");
    // Angka + kurung opsional di awal
    name = name.replace(/^\d+\s*(?:\(\s*\d+\s*\))?\s*[\.\-_]?\s*/, "");
    // Buang spasi akhir / pemisah sisa
    name = name.replace(/^[\s\.\-_]+/, "").replace(/[\s\.\-_]+$/, "");
    return name.toLowerCase();
}

// Kunci normalisasi lengkap untuk "Mode Persis":
// nomor utama + sisa nama (tanpa separator). Contoh "21. NIZAM" -> "21:nizam"
function buildExactKey(rawName) {
    var num = extractLeadingNumber(rawName);
    var rest = stripLeadingNumber(rawName).replace(/\s+/g, "");
    return num + ":" + rest;
}

// Pop-up pemilihan mode untuk Replace Revisi.
// return "exact" (Persis) / "flex" (Fleksibel) / null (Batal)
function askRevisiMode() {
    var d = new Window("dialog", "Mode Replace Revisi");
    d.orientation = "column";
    d.alignChildren = ["fill", "top"];
    d.spacing = 10;
    d.margins = 16;
    d.preferredSize.width = 460;

    var lblInfo = d.add("statictext", undefined,
        "Pilih cara pencocokan Smart Object dengan file Input:\n\n" +
        "\u2022 PERSIS   : Nama Smart Object harus sama dengan nama file input\n" +
        "              (nomor + teks nama). Contoh '21. NIZAM' \u2194 '21. NIZAM.jpg'.\n\n" +
        "\u2022 FLEKSIBEL : Hanya mencocokkan NOMOR utama saja.\n" +
        "              Contoh '21. NIZAM' dicocokkan dengan '21.jpg' atau '21(1).jpg'.\n\n" +
        "Catatan nomor: '(21). NAMA' \u2192 21  |  '21(1). NAMA' \u2192 21  |  '21. NAMA' \u2192 21",
        { multiline: true });
    lblInfo.preferredSize.width = 430;

    var grpBtn = d.add("group");
    grpBtn.alignment = "center";
    grpBtn.spacing = 12;

    var btnExact = grpBtn.add("button", undefined, "PERSIS");
    btnExact.preferredSize = [130, 34];
    var btnFlex = grpBtn.add("button", undefined, "FLEKSIBEL");
    btnFlex.preferredSize = [130, 34];
    var btnCancel = grpBtn.add("button", undefined, "Batal", { name: "cancel" });
    btnCancel.preferredSize = [80, 34];

    var chosen = null;

    btnExact.onClick = function () { chosen = "exact"; d.close(1); };
    btnFlex.onClick = function () { chosen = "flex"; d.close(1); };
    btnCancel.onClick = function () { chosen = null; d.close(0); };

    d.center();
    d.show();
    return chosen;
}

function showScrollableAlert(title, message, optWidth, optHeight) {
    var wW = optWidth || 400;
    var wH = optHeight || 300;

    var dialog = new Window("dialog", title);
    dialog.orientation = "column";
    dialog.alignChildren = ["fill", "fill"];
    dialog.preferredSize = [wW, wH];

    var edittext = dialog.add("edittext", undefined, message, { multiline: true, scrolling: true, readonly: true });
    edittext.preferredSize = [wW - 20, wH - 60];

    var btnOk = dialog.add("button", undefined, "OK");
    btnOk.alignment = "center";
    btnOk.onClick = function () { dialog.close(); };

    dialog.center();
    dialog.show();
}

// ==========================================
// Laporan Gabungan (Queue Mode)
// Menampilkan SATU dialog untuk semua antrian, berisi 2 pemberitahuan:
//   1) RINGKASAN TOTAL  -> total antrian, sukses, gagal, dll.
//   2) RINCIAN PER-ANTRIAN -> detail tiap job (tanpa klik OK berkali-kali).
// ==========================================
function showCombinedReport(results, mode) {
    var isRevisi = (mode === "revisi");

    // --- Hitung total ---
    var totalJobs = results.length;
    var totalSuccessFiles = 0;
    var totalFailFiles = 0;
    var totalSmartReplaced = 0;
    var totalSmartObjects = 0;
    var skippedJobs = 0;

    for (var i = 0; i < results.length; i++) {
        var r = results[i];
        totalSuccessFiles += (r.success ? r.success.length : 0);
        totalFailFiles += (r.fail ? r.fail.length : 0);
        if (r.replacedCount) totalSmartReplaced += r.replacedCount;
        if (r.totalSmartObjects) totalSmartObjects += r.totalSmartObjects;
        if (r.skipped) skippedJobs++;
    }

    var jobSukses = totalJobs - skippedJobs; // job yang benar-benar diproses

    // --- PEMBERITAHUAN 1: RINGKASAN ---
    var lines = [];
    lines.push("==================================================");
    lines.push(" PEMBERITAHUAN 1 : RINGKASAN TOTAL");
    lines.push("==================================================");
    lines.push("Mode          : " + (isRevisi ? ("REPLACE REVISI (" + ((results[0] && results[0].modeLabel) || "-") + ")") : "REPLACE STANDARD"));
    lines.push("Total Antrian : " + totalJobs);
    lines.push("Dijalankan    : " + jobSukses + (skippedJobs > 0 ? ("  (" + skippedJobs + " di-skip)") : ""));
    lines.push("File Berhasil : " + totalSuccessFiles);
    lines.push("File Gagal    : " + totalFailFiles);
    if (isRevisi) {
        lines.push("Smart Obj Ganti : " + totalSmartReplaced + " / " + totalSmartObjects + " ditemukan");
    }
    lines.push("");
    lines.push("==================================================");
    lines.push(" PEMBERITAHUAN 2 : RINCIAN PER-ANTRIAN");
    lines.push("==================================================");

    // --- PEMBERITAHUAN 2: RINCIAN PER JOB ---
    for (var j = 0; j < results.length; j++) {
        var res = results[j];
        lines.push("");
        lines.push("--------------------------------------------------");
        lines.push("#" + (j + 1) + "  " + res.masterName);
        lines.push("--------------------------------------------------");

        if (res.skipped) {
            lines.push("  [SKIP] " + (res.skipMsg || "Job dilewati.").replace(/\n/g, "\n         "));
            continue;
        }

        lines.push("  Berhasil : " + res.success.length + (res.success.length > 0 ? " file" : ""));
        for (var s = 0; s < res.success.length; s++) {
            lines.push("      + " + res.success[s]);
        }

        lines.push("  Gagal    : " + res.fail.length + (res.fail.length > 0 ? " file" : ""));
        for (var f = 0; f < res.fail.length; f++) {
            lines.push("      - " + res.fail[f]);
        }

        if (isRevisi) {
            lines.push("  Smart Obj: " + (res.replacedCount || 0) + " diganti");
        }
    }

    lines.push("");
    lines.push("==================================================");
    lines.push(" SEMUA ANTRIAN SELESAI.");
    lines.push("==================================================");

    // Kirim laporan gabungan ke BMachine (format sama seperti per-job)
    try {
        var bmReport = [];
        bmReport.push("=== LAPORAN ANTRIAN ===");
        bmReport.push("Total Antrian: " + totalJobs);
        bmReport.push("File Berhasil: " + totalSuccessFiles);
        bmReport.push("File Gagal: " + totalFailFiles);
        if (isRevisi) bmReport.push("Smart Obj Ganti: " + totalSmartReplaced);
        bmReport = bmReport.concat(lines);
        var bf = new File(Folder.temp + "/bmachine_result.json");
        bf.open("w");
        bf.encoding = "UTF-8";
        var escapedBm = [];
        for (var b = 0; b < bmReport.length; b++) {
            escapedBm.push('"' + bmReport[b].replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"');
        }
        bf.write('{"type":"result","title":"Replacer Summary (Antrian)","lines":[' + escapedBm.join(',') + ']}');
        bf.close();
    } catch (e) { }

    showScrollableAlert("Laporan Replace (Antrian)", lines.join("\n"), 560, 460);
}
