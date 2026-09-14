// @target photoshop

// === Settings Persistence ===
var DEFAULT_BASE_INPUT = "\\\\delapanmataair\\Editor 5\\2. REGULER\\#PROJECT SEKOLAH\\2026-2027";

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
    if (settingsFile.exists) {
        try {
            settingsFile.open("r");
            var content = settingsFile.read();
            settingsFile.close();
            var data = eval("(" + content + ")");
            if (!data.baseInputServer) data.baseInputServer = DEFAULT_BASE_INPUT;
            return data;
        } catch (e) { }
    }
    return { x: -1, y: -1, baseInputServer: DEFAULT_BASE_INPUT };
}

function saveSettings(x, y, baseInputServer) {
    var settingsFile = new File(Folder.userData + "/replacer_settings_v2.json");
    try {
        var base = baseInputServer || DEFAULT_BASE_INPUT;
        var escapedBase = base.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
        settingsFile.open("w");
        settingsFile.write('{"x":' + x + ',"y":' + y + ',"baseInputServer":"' + escapedBase + '"}');
        settingsFile.close();
    } catch (e) { }
}

// === Path Helpers & Auto-Detect Logic ===
function normalizePath(p) {
    if (!p) return "";
    var s = decodeURI(p);
    s = s.replace(/^["']+|["']+$/g, ""); // Strip surrounding quotes
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

function parseMasterPath(rawPath) {
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

    // Regex format Bulan: misal "03 SEPTEMBER 2026" atau "02 AGUSTUS 2025"
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

function autoDetectInputFolder(masterPath, baseServer) {
    var parsed = parseMasterPath(masterPath);
    if (!parsed) {
        return { found: false, path: "", schoolName: "-", targetItem: "-", message: "Format path belum lengkap" };
    }

    var baseNorm = normalizePath(baseServer);
    var baseFolder = new Folder(baseNorm);
    var serverReachable = baseFolder.exists;

    // Jika folder bulan ditemukan dalam path
    if (serverReachable && parsed.monthIndex !== -1 && parsed.monthToSchool !== "") {
        // 1. Jalur Utama: baseServer / Month / ... / School / PILIHAN / TargetItem
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

        // 2. Pencarian Fuzzy di dalam folder PILIHAN (mencocokkan nama subfolder)
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

        // 3. Jalur Langsung tanpa subfolder PILIHAN (di bawah sekolah)
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

    // 4. Pengecekan Sibling Lokal (jika folder master dan input ada di disk lokal yang sama)
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

    // Prefer the newest bmachine_context_*.json, fall back to fixed name.
    tempFile = findLatestContextFile();
    if (tempFile !== null && tempFile.exists) {
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
    var currentBaseServer = settings.baseInputServer || DEFAULT_BASE_INPUT;

    var w = new Window("dialog", "Smart Object Replacer (Queue Mode)");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 10;
    w.margins = 16;
    w.preferredSize.width = 540;

    // ==========================================
    // PANEL 1: MASTER (.PSD) & INFORMASI TARGET
    // ==========================================
    var pnlMaster = w.add("panel", undefined, " 1. FOLDER MASTER (.PSD) ");
    pnlMaster.orientation = "column";
    pnlMaster.alignChildren = ["fill", "top"];
    pnlMaster.spacing = 8;
    pnlMaster.margins = 12;

    // Row: Input Text & Buttons
    var grpMasterRow = pnlMaster.add("group");
    grpMasterRow.orientation = "row";
    grpMasterRow.alignChildren = ["fill", "center"];

    var lblMaster = grpMasterRow.add("statictext", undefined, "Master (.psd):");
    lblMaster.preferredSize.width = 85;

    var txtMaster = grpMasterRow.add("edittext", undefined, defaultMasterPath);
    txtMaster.preferredSize.width = 310;
    txtMaster.helpTip = "Drag & Drop folder dari Explorer ke sini, atau Paste (Ctrl+V)";

    var btnClearMaster = grpMasterRow.add("button", undefined, "X");
    btnClearMaster.size = [26, 26];
    btnClearMaster.helpTip = "Hapus text Master";

    var btnBrowseMaster = grpMasterRow.add("button", undefined, "Browse...");
    btnBrowseMaster.preferredSize.width = 75;

    // Hint Copy-Paste
    var lblMasterTip = pnlMaster.add("statictext", undefined, "Tip: Di Explorer klik folder lalu tekan Ctrl+Shift+C (Copy Path), lalu di sini langsung tekan Ctrl+V.");
    try {
        lblMasterTip.graphics.foregroundColor = w.graphics.newPen(w.graphics.PenType.SOLID_COLOR, [0.4, 0.4, 0.4, 1], 1);
    } catch (e) { }

    // Card Informasi Master & Target
    var pnlMasterInfo = pnlMaster.add("panel", undefined, "Informasi Master & Target");
    pnlMasterInfo.orientation = "column";
    pnlMasterInfo.alignChildren = ["fill", "top"];
    pnlMasterInfo.spacing = 4;
    pnlMasterInfo.margins = 8;

    var grpSekolah = pnlMasterInfo.add("group");
    grpSekolah.orientation = "row";
    var lblSekolahTitle = grpSekolah.add("statictext", undefined, "Sekolah :");
    lblSekolahTitle.preferredSize.width = 75;
    var lblSekolahVal = grpSekolah.add("statictext", undefined, "-");
    lblSekolahVal.preferredSize.width = 400;

    var grpTarget = pnlMasterInfo.add("group");
    grpTarget.orientation = "row";
    var lblTargetTitle = grpTarget.add("statictext", undefined, "Target  :");
    lblTargetTitle.preferredSize.width = 75;
    var lblTargetVal = grpTarget.add("statictext", undefined, "-");
    lblTargetVal.preferredSize.width = 400;

    var grpMasterCount = pnlMasterInfo.add("group");
    grpMasterCount.orientation = "row";
    var lblMasterCountTitle = grpMasterCount.add("statictext", undefined, "Master  :");
    lblMasterCountTitle.preferredSize.width = 75;
    var lblMasterCountVal = grpMasterCount.add("statictext", undefined, "-");
    lblMasterCountVal.preferredSize.width = 400;


    // ==========================================
    // PANEL 2: INPUT FOTO (SERVER / SELEKSI)
    // ==========================================
    var pnlInput = w.add("panel", undefined, " 2. FOLDER INPUT FOTO (SERVER / SELEKSI) ");
    pnlInput.orientation = "column";
    pnlInput.alignChildren = ["fill", "top"];
    pnlInput.spacing = 8;
    pnlInput.margins = 12;

    // Row: Input Text & Buttons
    var grpInputRow = pnlInput.add("group");
    grpInputRow.orientation = "row";
    grpInputRow.alignChildren = ["fill", "center"];

    var lblInput = grpInputRow.add("statictext", undefined, "Input (Img):");
    lblInput.preferredSize.width = 85;

    var txtInput = grpInputRow.add("edittext", undefined, defaultInputPath);
    txtInput.preferredSize.width = 310;
    txtInput.helpTip = "Folder foto sumber (otomatis terhubung atau pilih manual)";

    var btnClearInput = grpInputRow.add("button", undefined, "X");
    btnClearInput.size = [26, 26];
    btnClearInput.helpTip = "Hapus text Input";

    var btnBrowseInput = grpInputRow.add("button", undefined, "Browse...");
    btnBrowseInput.preferredSize.width = 75;

    // Card Informasi Input & Status
    var pnlInputInfo = pnlInput.add("panel", undefined, "Informasi Input & Status Sinkronisasi");
    pnlInputInfo.orientation = "column";
    pnlInputInfo.alignChildren = ["fill", "top"];
    pnlInputInfo.spacing = 4;
    pnlInputInfo.margins = 8;

    var grpInputCount = pnlInputInfo.add("group");
    grpInputCount.orientation = "row";
    var lblInputCountTitle = grpInputCount.add("statictext", undefined, "Input   :");
    lblInputCountTitle.preferredSize.width = 75;
    var lblInputCountVal = grpInputCount.add("statictext", undefined, "-");
    lblInputCountVal.preferredSize.width = 400;

    var grpStatus = pnlInputInfo.add("group");
    grpStatus.orientation = "row";
    var lblStatusTitle = grpStatus.add("statictext", undefined, "Status  :");
    lblStatusTitle.preferredSize.width = 75;
    var lblDetectStatus = grpStatus.add("statictext", undefined, "Menunggu input Master (.psd)...");
    lblDetectStatus.preferredSize.width = 400;

    // Bottom Actions on Input: Server Config & Add to Queue
    var grpInputActions = pnlInput.add("group");
    grpInputActions.orientation = "row";
    grpInputActions.alignChildren = ["fill", "center"];

    var btnServerConfig = grpInputActions.add("button", undefined, "Base Server...");
    btnServerConfig.preferredSize.width = 105;
    btnServerConfig.preferredSize.height = 26;
    btnServerConfig.helpTip = "Lihat atau ubah folder Base Server untuk auto-detect Input";

    var grpSpacer = grpInputActions.add("group");
    grpSpacer.alignment = ["fill", "fill"];

    var btnAddQueue = grpInputActions.add("button", undefined, "+ Tambah ke Antrian");
    btnAddQueue.preferredSize.width = 160;
    btnAddQueue.preferredSize.height = 26;
    btnAddQueue.helpTip = "Tambahkan pasangan Master dan Input ini ke Antrian (Queue)";


    // ==========================================
    // LOGIKA AUTO-DETECT & UPDATE REALTIME
    // ==========================================
    function updateMasterInfoOnly(raw) {
        if (!raw || raw.replace(/\s+/g, "") === "") {
            lblSekolahVal.text = "-";
            lblTargetVal.text = "-";
            lblMasterCountVal.text = "-";
            return;
        }

        var parsed = parseMasterPath(raw);
        if (parsed) {
            lblSekolahVal.text = parsed.schoolName || "-";
            lblTargetVal.text = parsed.targetItem || "-";
        } else {
            lblSekolahVal.text = "-";
            lblTargetVal.text = "-";
        }

        var mCount = countFilesFast(raw, /\.(psd|psb)$/i);
        if (mCount > 0) {
            lblMasterCountVal.text = mCount + " file .psd ditemukan";
        } else if (new Folder(raw).exists) {
            lblMasterCountVal.text = "0 file .psd (folder kosong)";
        } else {
            lblMasterCountVal.text = "- (folder belum ada)";
        }
    }

    function updateInputInfoOnly(raw) {
        if (!raw || raw.replace(/\s+/g, "") === "") {
            lblInputCountVal.text = "-";
            return;
        }
        var iCount = countFilesFast(raw, /\.(png|jpe?g|psd)$/i);
        if (iCount > 0) {
            lblInputCountVal.text = iCount + " file gambar ditemukan (termasuk di dalam subfolder)";
        } else if (new Folder(raw).exists) {
            lblInputCountVal.text = "0 file gambar di folder ini";
        } else {
            lblInputCountVal.text = "-";
        }
    }

    function triggerAutoDetect() {
        var raw = txtMaster.text;
        if (!raw || raw.replace(/\s+/g, "") === "") {
            lblSekolahVal.text = "-";
            lblTargetVal.text = "-";
            lblMasterCountVal.text = "-";
            lblDetectStatus.text = "Menunggu input Master (.psd)...";
            lblInputCountVal.text = "-";
            return;
        }

        var normalized = toWindowsPath(normalizePath(raw));
        if (normalized && normalized !== raw && raw.indexOf("\\") !== -1) {
            txtMaster.text = normalized;
            raw = normalized;
        }

        updateMasterInfoOnly(raw);

        var res = autoDetectInputFolder(raw, currentBaseServer);
        lblDetectStatus.text = res.message;

        if (res.found && res.path) {
            txtInput.text = res.path;
            updateInputInfoOnly(res.path);
        } else {
            updateInputInfoOnly(txtInput.text);
        }
    }

    // Event Handlers (Instant OnChanging saat Paste/Drop)
    txtMaster.onChanging = function () {
        triggerAutoDetect();
    };

    txtMaster.onChange = function () {
        triggerAutoDetect();
    };

    txtInput.onChanging = function () {
        updateInputInfoOnly(txtInput.text);
    };

    txtInput.onChange = function () {
        updateInputInfoOnly(txtInput.text);
    };

    btnBrowseMaster.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Master");
        if (f) {
            txtMaster.text = decodeURI(f.fsName || f.fullName);
            triggerAutoDetect();
        }
    };

    btnClearMaster.onClick = function () {
        txtMaster.text = "";
        triggerAutoDetect();
        txtMaster.active = true;
    };

    btnBrowseInput.onClick = function () {
        var f = Folder.selectDialog("Pilih Folder Seleksi");
        if (f) {
            txtInput.text = decodeURI(f.fsName || f.fullName);
            updateInputInfoOnly(txtInput.text);
        }
    };

    btnClearInput.onClick = function () {
        txtInput.text = "";
        updateInputInfoOnly("");
        txtInput.active = true;
    };

    btnServerConfig.onClick = function () {
        var promptMsg = "Base Path Server Input saat ini:\n\n" + currentBaseServer + "\n\nMasukkan path baru jika ingin mengubah:";
        var newBase = prompt(promptMsg, currentBaseServer);
        if (newBase !== null && newBase !== "") {
            newBase = newBase.replace(/^["']+|["']+$/g, "").replace(/[\\\/]+$/, "");
            currentBaseServer = newBase;
            saveSettings(w.location.x, w.location.y, currentBaseServer);
            triggerAutoDetect();
        }
    };

    // Auto-detect saat pertama kali dialog terbuka jika Master sudah terisi
    if (txtMaster.text != "") {
        triggerAutoDetect();
    }

    // ==========================================
    // PANEL 3: DAFTAR ANTRIAN (QUEUE)
    // ==========================================
    var grpQueue = w.add("panel", undefined, " 3. DAFTAR ANTRIAN KERJA ");
    grpQueue.orientation = "column";
    grpQueue.alignChildren = ["fill", "top"];
    grpQueue.visible = false;
    grpQueue.spacing = 6;
    grpQueue.margins = 10;

    var lblQueue = grpQueue.add("statictext", undefined, "Daftar Antrian (Queue):");
    var listQueue = grpQueue.add("listbox", undefined, [], { multiselect: true });
    listQueue.preferredSize.height = 110;
    listQueue.preferredSize.width = 500;

    // Queue Controls
    var grpQueueControl = grpQueue.add("group");
    grpQueueControl.orientation = "row";
    grpQueueControl.alignChildren = ["left", "center"];

    var btnClearQueue = grpQueueControl.add("button", undefined, "Hapus Terpilih");
    btnClearQueue.size = [110, 25];
    btnClearQueue.enabled = false;

    // Logic Add Queue
    var queueData = []; // Store real objects {master, input}

    btnAddQueue.onClick = function () {
        if (txtMaster.text != "" && txtInput.text == "") {
            triggerAutoDetect();
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

        // Add to UI
        var label = "M: " + new File(txtMaster.text).displayName + " | I: " + new File(txtInput.text).displayName;
        listQueue.add("item", label);

        // Update UI state
        grpQueue.text = " 3. DAFTAR ANTRIAN KERJA (" + queueData.length + ") ";
        grpQueue.visible = true;
        w.layout.layout(true); // Refresh layout

        // Clear fields for next entry
        txtInput.text = "";
        txtMaster.text = "";
        triggerAutoDetect();
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

        grpQueue.text = " 3. DAFTAR ANTRIAN KERJA (" + queueData.length + ") ";
        if (listQueue.items.length == 0) {
            grpQueue.visible = false;
            w.layout.layout(true);
        }
    };

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

    // === EXECUTION LOGIC ===
    btnRun.onClick = function () {
        w.close(1); // Standard Run
    };

    btnRevisi.onClick = function () {
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
    if (result != 1 && result != 2) return; // Cancel

    saveSettings(w.location.x, w.location.y, currentBaseServer);

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
        triggerAutoDetect();
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

    for (var j = 0; j < jobsToRun.length; j++) {
        var job = jobsToRun[j];
        var mFolder = new Folder(job.master);
        var iFolder = new Folder(job.input);

        if (!mFolder.exists || !iFolder.exists) {
            // Log error but continue
            continue;
        }

        if (result == 1) {
            // STANDARD REPLACE
            runReplacementLogic(mFolder, iFolder); // This function has its own alerts/summary. 
            // We should modify it to NOT alert per job if queue > 1?
            // Or just let it run. User will see progress per job.
        } else {
            // REVISI REPLACE
            runRevisiLogic(mFolder, iFolder);
        }
    }
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
        alert("Job Skipped (No files).\nMaster: " + templateCount + "\nInput: " + inputCount);
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
                // Hapus spasi di awal/akhir dan suffix " (1)" atau spasi banyak "    (2)"
                var inputBaseNameStripped = inputBaseName.replace(/^\s+|\s+$/g, "").replace(/\s*\(\d+\)$/, "");
                
                // Coba cocokan kalau format master hanya angka misal "1" dan input " (1)"
                var inputJustNumber = "";
                var numberMatch = inputBaseNameStripped.match(/(\d+)/) || inputBaseName.match(/\((\d+)\)/) || inputBaseName.match(/(\d+)/);
                if (numberMatch) inputJustNumber = numberMatch[1];

                if (inputBaseName === templateBaseName || inputBaseNameStripped === templateBaseName || (inputJustNumber !== "" && inputJustNumber === templateBaseName)) {
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

                doc.activeLayer = smartS;
                replaceSmartContent(matchedInputs[1]);
                smartS.name = matchedInputs[1].displayName.replace(/\.[^\.]+$/, "");

                successList.push(relPath(templateFolder, template));
            } else if (matchedInputs.length >= 1) {
                doc.activeLayer = smartXL;
                replaceSmartContent(matchedInputs[0]);
                smartXL.name = matchedInputs[0].displayName.replace(/\.[^\.]+$/, "");
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

    var msg = "Master: " + decodeURI(templateFolder.name) + "\n\n";
    msg += "Berhasil: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    showScrollableAlert("Laporan Replace (Standard)", msg);
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
function runRevisiLogic(masterFolder, inputFolder) {
    // Scan for PSD/PSB files in master folder
    var masterFiles = scanFolderForFiles(masterFolder, /\.(psd|psb)$/i);
    masterFiles.sort(sortByNumberInFilename);

    // Scan for replacement files in input folder
    var inputFiles = scanFolderForFiles(inputFolder, /\.(png|psd|jpe?g)$/i);

    if (masterFiles.length == 0) {
        alert("Job Skipped (No PSD/PSB in Master)!");
        return;
    }

    if (inputFiles.length == 0) {
        alert("Job Skipped (No Image in Input)!");
        return;
    }

    // Build a lookup map: filename (without extension) -> ARRAY of file objects
    var inputMap = {};


    for (var i = 0; i < inputFiles.length; i++) {
        var file = inputFiles[i];
        var baseName = file.displayName.replace(/\.[^\.]+$/, "").toLowerCase();

        if (!inputMap[baseName]) {
            inputMap[baseName] = [];
        }
        inputMap[baseName].push(file);

        // Tambahkan juga versi stripped (hilangkan spasi awal/akhir dan " (1)")
        var baseNameStripped = baseName.replace(/^\s+|\s+$/g, "").replace(/\s*\(\d+\)$/, "");
        if (baseName !== baseNameStripped) {
            if (!inputMap[baseNameStripped]) {
                inputMap[baseNameStripped] = [];
            }
            inputMap[baseNameStripped].push(file);
        }
        
        // Tambahkan versi "hanya angka" jika nama formatnya "   (2)"
        var numberMatch = baseNameStripped.match(/(\d+)/) || baseName.match(/\((\d+)\)/) || baseName.match(/(\d+)/);
        if (numberMatch) {
            var justNumber = numberMatch[1];
            if (baseName !== justNumber && baseNameStripped !== justNumber) {
                if (!inputMap[justNumber]) inputMap[justNumber] = [];
                inputMap[justNumber].push(file);
            }
        }
    }

    var successList = [];
    var failList = [];
    var totalSmartObjects = 0;
    var replacedCount = 0;

    // Helper to get relative directory path
    function getRelDir(file, root) {
        var rel = decodeURI(file.parent.fullName).replace(decodeURI(root.fullName), "");
        if (rel.indexOf("/") == 0) rel = rel.substring(1);
        return rel;
    }

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
                var smartName = smartObj.name.toLowerCase();

                // Look for matching files in inputMap
                var candidates = inputMap[smartName];

                if (candidates && candidates.length > 0) {
                    // Sort candidates to find best match
                    // Priority 1: Same relative directory
                    // Priority 2: File type (PNG > PSD > JPG)
                    candidates.sort(function (a, b) {
                        var aRel = getRelDir(a, inputFolder);
                        var bRel = getRelDir(b, inputFolder);
                        var aMatch = (aRel === masterRelDir);
                        var bMatch = (bRel === masterRelDir);

                        if (aMatch && !bMatch) return -1;
                        if (!aMatch && bMatch) return 1;

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
    msg += "Smart Object Replaced: " + replacedCount + "\n\n";
    msg += "File Berhasil: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "File Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    showScrollableAlert("Laporan Replace (Revisi)", msg);
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
