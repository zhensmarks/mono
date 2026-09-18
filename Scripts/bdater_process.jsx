#target photoshop

/***************************************************************
 * IDCARD AUTO & BDATER PROCESS ENGINE
 * Full Logic adapted from robot_idcard_experiment.jsx
 *
 * Struktur PSD yang diharapkan:
 *   LAYER n / ISI n  / <nama header> (Text Layer)
 *   LAYER n / FOTO n / XL atau S      (Smart Object)
 *   (Fallback: group DATA / ISI & group FOTO)
 *
 * Pencocokan foto & data ke PSD:
 *   - normalizeName: bersihkan ekstensi, nomor urut, label 'nama :',
 *     gelar setelah koma (s.substring(0, commaAt)), gelar tanpa koma
 *     (S.Pd., dr., Ir., dsb.), spasi & simbol.
 *   - buildFileBuckets: mendistribusikan kemunculan ganda (occurrence).
 *   - Mengisi layer teks sesuai nama header data.
 *   - Mengganti Smart Object foto via placedLayerReplaceContents
 *     dan me-rename layer menjadi nama file foto.
 *   - Dokumen yang berhasil diproses dibiarkan terbuka (belum disimpan).
 ***************************************************************/

(function () {
    var SCRIPT_FILE = new File($.fileName);
    var ROOT = SCRIPT_FILE.parent;

    var oldDialogs = app.displayDialogs;
    var oldRulerUnits = app.preferences.rulerUnits;
    app.displayDialogs = DialogModes.NO;
    app.preferences.rulerUnits = Units.PIXELS;

    function trimText(value) {
        return value == null ? "" : String(value).replace(/^\s+|\s+$/g, "");
    }

    function decodeName(value) {
        try { return decodeURI(String(value)); }
        catch (e) { return String(value); }
    }

    function normalizeName(value) {
        if (value == null) return "";
        var s = decodeName(value).toLowerCase();
        s = s.replace(/\.(psd|psb|jpe?g|png|tiff?|bmp)$/i, "");
        s = s.replace(/^\s*\(\s*\d+\s*\)\s*/, "");
        s = s.replace(/^\s*\d+\s*[.\-_)]\s*/, "");
        // Label "Nama :" hanya dibuang untuk pencocokan; isi layer tetap memakai teks asli
        s = s.replace(/^\s*nama\s*:\s*/i, "");
        // Buang gelar setelah koma (misal: "Budi Santoso, S.Kom." -> "budi santoso")
        var commaAt = s.indexOf(",");
        if (commaAt >= 0) s = s.substring(0, commaAt);
        // Buang gelar sederhana tanpa koma (misal: "Budi Santoso S.Pd.")
        s = s.replace(/\s+(?:(?:[smd]\.[a-z.]+)|(?:gr|dr|dra|drs|ir)\.?)\s*$/i, "");
        s = s.replace(/[._\-]+/g, " ");
        s = s.replace(/\s+/g, " ");
        return trimText(s);
    }

    // Aturan kualitas penggantian foto: PNG > PSD > JPG dengan nama file sama.
    // Pencocokan di BMachine memakai JPG saja, tapi ke PSD boleh pakai versi
    // kualitas terbaik yang tersedia.
    function pickBestPhoto(photoFile) {
        try {
            var stem = baseName(photoFile);
            var dirFs = photoFile.parent.fsName;
            var png = new File(dirFs + "/" + stem + ".png");
            if (png.exists) return png;
            var psd = new File(dirFs + "/" + stem + ".psd");
            if (psd.exists) return psd;
        } catch (e) {}
        return photoFile;
    }

    function baseName(file) {
        return decodeName(file.name).replace(/\.[^.]+$/, "");
    }

    function fileExtension(file) {
        var match = String(file.name).toLowerCase().match(/\.([a-z0-9]+)$/);
        return match ? match[1] : "";
    }

    function quoteCommandArg(value) {
        return '"' + String(value).replace(/"/g, '""') + '"';
    }

    function readTextFile(path) {
        var f = new File(path);
        if (!f.exists) throw new Error("File tidak ditemukan: " + path);
        f.encoding = "UTF-8";
        if (!f.open("r")) throw new Error("Gagal membuka file: " + path);
        var txt = f.read();
        f.close();
        return txt;
    }

    function writeTextFile(path, content) {
        var f = new File(path);
        f.encoding = "UTF-8";
        if (!f.open("w")) throw new Error("Gagal menulis file: " + path);
        f.write(content);
        f.close();
    }

    function jsonParse(raw) {
        if (typeof JSON !== "undefined" && JSON.parse) return JSON.parse(raw);
        return eval("(" + raw + ")");
    }

    function jsonStringify(obj) {
        if (typeof JSON !== "undefined" && JSON.stringify) return JSON.stringify(obj, null, 2);
        function esc(s) {
            return ("" + s).replace(/\\/g, "\\\\").replace(/\"/g, "\\\"").replace(/\r/g, "\\r").replace(/\n/g, "\\n");
        }
        function ser(v) {
            if (v === null || v === undefined) return "null";
            if (typeof v === "string") return '"' + esc(v) + '"';
            if (typeof v === "number" || typeof v === "boolean") return "" + v;
            if (v instanceof Array) {
                var a = [];
                for (var i = 0; i < v.length; i++) a.push(ser(v[i]));
                return "[" + a.join(", ") + "]";
            }
            var o = [];
            for (var k in v) {
                if (v.hasOwnProperty(k)) o.push('"' + esc(k) + '": ' + ser(v[k]));
            }
            return "{" + o.join(", ") + "}";
        }
        return ser(obj);
    }

    function tryReadPointerConfigPath() {
        try {
            var ptr = new File(Folder.temp.fsName + "/YB_PROCESS_CONFIG.txt");
            if (!ptr.exists) return null;
            ptr.encoding = "UTF-8";
            if (!ptr.open("r")) return null;
            var p = trimText(ptr.read());
            ptr.close();
            return p || null;
        } catch (e) {
            return null;
        }
    }

    function prepareDataFile(selectedFile) {
        var ext = fileExtension(selectedFile);
        if (ext === "json") {
            return { file: selectedFile, temporary: false, excelInfo: "", isJson: true };
        }
        if (ext !== "xlsx" && ext !== "xlsm" && ext !== "xls") {
            return { file: selectedFile, temporary: false, excelInfo: "", isJson: false };
        }

        var helper = new File(ROOT.fsName + "/robot_idcard_excel.ps1");
        if (!helper.exists) {
            throw new Error("Helper Excel tidak ditemukan: " + helper.fsName);
        }

        var tempFile = new File(
            Folder.temp.fsName + "/robot_idcard_excel_" + (new Date().getTime()) + ".tsv"
        );
        var statusFile = new File(tempFile.fsName + ".status");
        var command =
            "powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " + quoteCommandArg(helper.fsName) +
            " -InputPath " + quoteCommandArg(selectedFile.fsName) +
            " -OutputPath " + quoteCommandArg(tempFile.fsName);
        app.system(command);
        var output = "";
        if (statusFile.exists) {
            statusFile.encoding = "UTF8";
            if (statusFile.open("r")) {
                output = trimText(statusFile.read());
                statusFile.close();
            }
            try { statusFile.remove(); } catch (statusCleanupError) {}
        }

        if (!tempFile.exists || output.indexOf("OK|") !== 0) {
            try { if (tempFile.exists) tempFile.remove(); } catch (cleanupError) {}
            throw new Error(
                "Excel tidak dapat dibaca.\n" +
                (output || "Pastikan Microsoft Excel terpasang dan workbook tidak rusak.")
            );
        }
        return { file: tempFile, temporary: true, excelInfo: output, isJson: false };
    }

    function splitDelimitedLine(line, delimiter) {
        var result = [];
        var current = "";
        var quoted = false;
        var i, ch;

        for (i = 0; i < line.length; i++) {
            ch = line.charAt(i);
            if (ch === '"') {
                if (quoted && line.charAt(i + 1) === '"') {
                    current += '"';
                    i++;
                } else {
                    quoted = !quoted;
                }
            } else if (ch === delimiter && !quoted) {
                result.push(current);
                current = "";
            } else {
                current += ch;
            }
        }
        result.push(current);
        return result;
    }

    function hasOpenQuote(text) {
        var quoted = false;
        for (var i = 0; i < text.length; i++) {
            if (text.charAt(i) !== '"') continue;
            if (quoted && text.charAt(i + 1) === '"') {
                i++;
            } else {
                quoted = !quoted;
            }
        }
        return quoted;
    }

    function detectDelimiter(line) {
        var tabs = (line.match(/\t/g) || []).length;
        var semicolons = (line.match(/;/g) || []).length;
        var commas = (line.match(/,/g) || []).length;
        if (tabs >= semicolons && tabs >= commas && tabs > 0) return "\t";
        if (semicolons >= commas && semicolons > 0) return ";";
        if (commas > 0) return ",";
        return null;
    }

    function findHeaderIndex(headers, wanted) {
        var target = trimText(wanted).toLowerCase();
        for (var i = 0; i < headers.length; i++) {
            if (trimText(headers[i]).toLowerCase() === target) return i;
        }
        return -1;
    }

    function summarizeDataEntry(entry, headers) {
        var parts = [];
        for (var i = 0; i < headers.length; i++) {
            var header = trimText(headers[i]);
            if (!header || header.toLowerCase() === "nama") continue;
            var value = trimText(entry[header]).replace(/[\r\n]+/g, " / ");
            if (!value) continue;
            if (value.length > 140) value = value.substring(0, 137) + "...";
            parts.push(header + ": " + value);
            if (parts.length >= 3) break;
        }
        return parts.length ? parts.join(" | ") : "(data lainnya kosong)";
    }

    function dataEntriesEqual(a, b, headers) {
        for (var i = 0; i < headers.length; i++) {
            var header = trimText(headers[i]);
            if (trimText(a[header]) !== trimText(b[header])) return false;
        }
        return true;
    }

    function extractNameFromEntry(entry, headers, nameIndex) {
        if (nameIndex >= 0) {
            var direct = trimText(entry[trimText(headers[nameIndex])]);
            if (direct) {
                var directLines = direct.split(/[\r\n]+/);
                for (var directIndex = 0; directIndex < directLines.length; directIndex++) {
                    if (trimText(directLines[directIndex])) return trimText(directLines[directIndex]);
                }
            }
        }

        // Fallback jika kolom NAMA bernama variasi seperti "Nama Siswa"
        for (var h = 0; h < headers.length; h++) {
            var col = trimText(headers[h]).toLowerCase();
            if (col === "nama siswa" || col === "nama guru" || col === "nama lengkap" || col === "student name") {
                var dVal = trimText(entry[trimText(headers[h])]);
                if (dVal) return dVal;
            }
        }

        // Fallback untuk tabel satu kolom seperti DATA yang di dalam selnya mempunyai baris "Nama : ..."
        for (var i = 0; i < headers.length; i++) {
            var value = trimText(entry[trimText(headers[i])]);
            if (!value) continue;
            var match = /(?:^|[\r\n])\s*nama\s*:\s*(.+?)(?=[\r\n]|\s+(?:jabatan|ttl|tempat\s+lahir|tanggal\s+lahir|alamat|kelas|nis)\s*:|$)/i.exec(value);
            if (match && trimText(match[1])) return trimText(match[1]);
        }

        // Fallback: kolom DATA tanpa label "Nama :", gunakan baris pertama sel DATA
        var dataIndex = findHeaderIndex(headers, "DATA");
        if (dataIndex >= 0) {
            var dataValue = trimText(entry[trimText(headers[dataIndex])]);
            if (dataValue) {
                var lines = dataValue.split(/[\r\n]+/);
                for (var lineIndex = 0; lineIndex < lines.length; lineIndex++) {
                    if (trimText(lines[lineIndex])) return trimText(lines[lineIndex]);
                }
            }
        }
        return "";
    }

    function readDataFromJson(file) {
        var raw = readTextFile(file.fsName);
        var parsed = jsonParse(raw);
        var headers = parsed.header || [];
        var rows = parsed.rows || [];
        var byName = {};
        var repeatedNames = [];
        var nameIndex = findHeaderIndex(headers, "Nama");
        var rowsWithoutMatchName = 0;

        for (var r = 0; r < rows.length; r++) {
            var entry = rows[r];
            var originalName = extractNameFromEntry(entry, headers, nameIndex);
            var key = normalizeName(originalName);
            if (!key) {
                rowsWithoutMatchName++;
                continue;
            }

            if (!byName[key]) byName[key] = [];
            if (byName[key].length) {
                var firstEntry = byName[key][0];
                repeatedNames.push(
                    originalName + " (kemunculan " + (byName[key].length + 1) + ")" +
                    "\n    Status: " + (dataEntriesEqual(firstEntry, entry, headers) ? "seluruh baris sama" : "isi baris berbeda") +
                    "\n    Baris 1: " + summarizeDataEntry(firstEntry, headers) +
                    "\n    Baris " + (byName[key].length + 1) + ": " + summarizeDataEntry(entry, headers)
                );
            }
            byName[key].push(entry);
        }

        return { headers: headers, byName: byName, repeatedNames: repeatedNames, totalRows: rows.length };
    }

    function readDataFile(file) {
        var ext = fileExtension(file);
        if (ext === "json") {
            return readDataFromJson(file);
        }

        var byName = {};
        var repeatedNames = [];
        file.encoding = "UTF8";
        if (!file.open("r")) throw new Error("File data tidak dapat dibuka.");

        if (file.eof) {
            file.close();
            throw new Error("File data kosong.");
        }

        var headerLine = file.readln();
        if (headerLine && headerLine.charCodeAt(0) === 0xFEFF) {
            headerLine = headerLine.substring(1);
        }

        var delimiter = detectDelimiter(headerLine);
        var headers = delimiter ? splitDelimitedLine(headerLine, delimiter) : [headerLine];
        var nameIndex = findHeaderIndex(headers, "Nama");
        var rowsWithoutMatchName = 0;
        var totalRows = 0;

        while (!file.eof) {
            var line = file.readln();
            if (!trimText(line)) continue;

            while (hasOpenQuote(line) && !file.eof) {
                line += "\r" + file.readln();
            }

            var cols = delimiter ? splitDelimitedLine(line, delimiter) : splitDelimitedLine(line, null);
            var entry = {};
            for (var c = 0; c < headers.length; c++) {
                entry[trimText(headers[c])] = trimText(cols[c]);
            }

            var originalName = extractNameFromEntry(entry, headers, nameIndex);
            var key = normalizeName(originalName);
            totalRows++;
            if (!key) {
                rowsWithoutMatchName++;
                continue;
            }

            if (!byName[key]) byName[key] = [];
            if (byName[key].length) {
                var firstEntry = byName[key][0];
                repeatedNames.push(
                    originalName + " (kemunculan " + (byName[key].length + 1) + ")" +
                    "\n    Status: " + (dataEntriesEqual(firstEntry, entry, headers) ? "seluruh baris sama" : "isi baris berbeda") +
                    "\n    Baris 1: " + summarizeDataEntry(firstEntry, headers) +
                    "\n    Baris " + (byName[key].length + 1) + ": " + summarizeDataEntry(entry, headers)
                );
            }
            byName[key].push(entry);
        }

        file.close();
        if (rowsWithoutMatchName > 0) {
            throw new Error(
                rowsWithoutMatchName + " baris tidak mempunyai identitas. " +
                "Sediakan header NAMA atau teks 'Nama : ...' di dalam salah satu kolom."
            );
        }
        return { headers: headers, byName: byName, repeatedNames: repeatedNames, totalRows: totalRows };
    }

    function listFiles(folder, pattern) {
        var files = [];

        function scan(currentFolder) {
            var items = currentFolder.getFiles();
            for (var i = 0; i < items.length; i++) {
                if (items[i] instanceof File && pattern.test(items[i].name)) {
                    files.push(items[i]);
                } else if (items[i] instanceof Folder) {
                    scan(items[i]);
                }
            }
        }

        scan(folder);
        files.sort(function (a, b) {
            function orderNumber(name) {
                var m = /^\s*\(\s*(\d+)\s*\)/.exec(decodeName(name));
                if (!m) m = /^\s*(\d+)/.exec(decodeName(name));
                return m ? parseInt(m[1], 10) : 999999;
            }

            var an = orderNumber(a.name), bn = orderNumber(b.name);
            if (an !== bn) return an - bn;

            var ak = normalizeName(a.name);
            var bk = normalizeName(b.name);
            if (ak < bk) return -1;
            if (ak > bk) return 1;

            var af = decodeName(a.fsName).toLowerCase();
            var bf = decodeName(b.fsName).toLowerCase();
            return af < bf ? -1 : (af > bf ? 1 : 0);
        });
        return files;
    }

    function buildFileBuckets(files) {
        var byName = {};
        for (var i = 0; i < files.length; i++) {
            var key = normalizeName(files[i].name);
            if (!key) continue;
            if (!byName[key]) byName[key] = [];
            byName[key].push(files[i]);
        }
        return byName;
    }

    function compactLayerName(value) {
        return trimText(value).toLowerCase().replace(/[\s._-]+/g, "");
    }

    function findCompactLayerSet(parent, wantedName) {
        var wanted = compactLayerName(wantedName);
        for (var i = 0; i < parent.layerSets.length; i++) {
            if (compactLayerName(parent.layerSets[i].name) === wanted) {
                return parent.layerSets[i];
            }
        }
        return null;
    }

    function findPhotoSmartObject(photoGroup) {
        // Priority 1: Layer named "XL" (exact robot_idcard_experiment.jsx standard)
        for (var i = 0; i < photoGroup.artLayers.length; i++) {
            var layer = photoGroup.artLayers[i];
            if (layer.kind === LayerKind.SMARTOBJECT &&
                trimText(layer.name).toLowerCase() === "xl") {
                return layer;
            }
        }
        // Priority 2: Layer named "S" (BMachine replace.jsx parity)
        for (var j = 0; j < photoGroup.artLayers.length; j++) {
            var layerS = photoGroup.artLayers[j];
            if (layerS.kind === LayerKind.SMARTOBJECT &&
                trimText(layerS.name).toLowerCase() === "s") {
                return layerS;
            }
        }
        // Priority 3: Any Smart Object layer inside photoGroup
        for (var k = 0; k < photoGroup.artLayers.length; k++) {
            var layerAny = photoGroup.artLayers[k];
            if (layerAny.kind === LayerKind.SMARTOBJECT) {
                return layerAny;
            }
        }
        return null;
    }

    function findTextLayerDeep(parent, wantedName) {
        var wanted = trimText(wantedName).toLowerCase();
        var i, layer, hit;
        for (i = 0; i < parent.artLayers.length; i++) {
            layer = parent.artLayers[i];
            if (layer.kind === LayerKind.TEXT &&
                trimText(layer.name).toLowerCase() === wanted) {
                return layer;
            }
        }
        for (i = 0; i < parent.layerSets.length; i++) {
            hit = findTextLayerDeep(parent.layerSets[i], wantedName);
            if (hit) return hit;
        }
        return null;
    }

    function getEntryField(entry, header) {
        var wanted = trimText(header).toLowerCase();
        for (var field in entry) {
            if (trimText(field).toLowerCase() === wanted) return entry[field];
        }
        return "";
    }

    function fieldSelected(name, fields) {
        if (!fields || !fields.length) return true;
        var wanted = trimText(name).toLowerCase();
        for (var f = 0; f < fields.length; f++) {
            if (trimText(fields[f]).toLowerCase() === wanted) return true;
        }
        return false;
    }

    function getTemplateLayers(doc) {
        // 1. Standar robot_idcard_experiment.jsx: LAYER n > ISI n dan FOTO n
        var candidates = [];
        for (var i = 0; i < doc.layerSets.length; i++) {
            var rootGroup = doc.layerSets[i];
            var match = /^layer(\d+)$/i.exec(compactLayerName(rootGroup.name));
            if (match) candidates.push({ number: parseInt(match[1], 10), group: rootGroup });
        }
        candidates.sort(function (a, b) { return a.number - b.number; });

        for (var c = 0; c < candidates.length; c++) {
            var n = candidates[c].number;
            var isi = findCompactLayerSet(candidates[c].group, "ISI " + n);
            var foto = findCompactLayerSet(candidates[c].group, "FOTO " + n);
            var photoLayer = null;
            if (foto) photoLayer = findPhotoSmartObject(foto);
            if (revisionMode ? isi : (isi && foto && photoLayer)) {
                return { number: n, isi: isi, foto: photoLayer };
            }
        }

        // 2. Fallback untuk layout single-slot (misal group DATA / ISI & FOTO di root level)
        var fallbackIsi = findCompactLayerSet(doc, "ISI") || findCompactLayerSet(doc, "DATA");
        var fallbackFoto = findCompactLayerSet(doc, "FOTO");
        var fbPhotoLayer = null;
        if (fallbackFoto) fbPhotoLayer = findPhotoSmartObject(fallbackFoto);
        if (fallbackIsi && (revisionMode || (fallbackFoto && fbPhotoLayer))) {
            return { number: 1, isi: fallbackIsi, foto: fbPhotoLayer };
        }

        throw new Error(
            "Struktur personalisasi tidak ditemukan. Harus ada pasangan " +
            "LAYER n > ISI n dan FOTO n (atau grup ISI/DATA dan FOTO)."
        );
    }

    function replaceSmartObjectContents(doc, layer, photoFile) {
        app.activeDocument = doc;
        doc.activeLayer = layer;
        var descriptor = new ActionDescriptor();
        descriptor.putPath(charIDToTypeID("null"), photoFile);
        executeAction(stringIDToTypeID("placedLayerReplaceContents"), descriptor, DialogModes.NO);

        // Agar foto yang masuk mudah diperiksa di panel Layers,
        // ubah nama Smart Object XL menjadi nama file foto tanpa ekstensi.
        try {
            doc.activeLayer.name = baseName(photoFile);
        } catch (renameError) {}
    }

    function locateDefaultDataFile() {
        var candidates = ROOT.getFiles(function (item) {
            return item instanceof File && /\.(xlsx|xlsm|xls|txt|tsv|csv|json)$/i.test(item.name) &&
                !/^LOG_IDCARD/i.test(item.name);
        });
        if (candidates.length === 1) return candidates[0];

        for (var e = 0; e < candidates.length; e++) {
            if (/\.xlsx$/i.test(candidates[e].name)) return candidates[e];
        }
        for (var i = 0; i < candidates.length; i++) {
            if (/export/i.test(candidates[i].name)) return candidates[i];
        }
        return null;
    }

    function chooseInputs() {
        // Mode otomatis dari BDater via %TEMP%/YB_PROCESS_CONFIG.txt
        var ptrPath = tryReadPointerConfigPath();
        if (ptrPath && new File(ptrPath).exists) {
            try {
                var cfgRaw = readTextFile(ptrPath);
                var cfg = jsonParse(cfgRaw);
                return {
                    dataFile: new File(cfg.data_json || cfg.dataPath),
                    photoFolder: new Folder(cfg.foto_dir || cfg.photoPath || Folder.temp.fsName),
                    psdFolder: new Folder(cfg.psd_dir || cfg.psdPath),
                    reportJsonPath: cfg.report_json || (Folder.temp.fsName + "/yb_process_report.json"),
                    photoFormat: "all",
                    autoConfirm: true,
                    operation: cfg.operation || "full",
                    fields: cfg.fields || []
                };
            } catch (eCfg) {}
        }

        // Mode global script variable (external testing)
        if ($.global.ROBOT_IDCARD_CONFIG) {
            var external = $.global.ROBOT_IDCARD_CONFIG;
            return {
                dataFile: new File(external.dataPath),
                photoFolder: new Folder(external.photoPath || Folder.temp.fsName),
                psdFolder: new Folder(external.psdPath),
                reportJsonPath: external.reportJsonPath || (Folder.temp.fsName + "/yb_process_report.json"),
                photoFormat: external.photoFormat || "all",
                autoConfirm: external.autoConfirm === true,
                operation: external.operation || "full",
                fields: external.fields || []
            };
        }

        // ScriptUI Dialog (jika dijalankan langsung secara manual dari dalam Photoshop)
        var win = new Window("dialog", "ID Card Otomatis");
        win.orientation = "column";
        win.alignChildren = "fill";

        function addPathRow(label, defaultValue, browseHandler) {
            var group = win.add("group");
            group.orientation = "row";
            group.add("statictext", undefined, label).preferredSize.width = 90;
            var input = group.add("edittext", undefined, defaultValue || "");
            input.characters = 54;
            var button = group.add("button", undefined, "Pilih...");
            button.onClick = function () {
                var selected = browseHandler();
                if (selected) input.text = selected.fsName;
            };
            return input;
        }

        var defaultData = locateDefaultDataFile();
        var defaultPhoto = new Folder(ROOT.fsName + "/PILIHAN");

        var dataInput = addPathRow(
            "Excel / data:",
            defaultData ? defaultData.fsName : "",
            function () {
                return File.openDialog(
                    "Pilih Excel atau data teks ID card",
                    "Data:*.xlsx;*.xlsm;*.xls;*.txt;*.tsv;*.csv;*.json"
                );
            }
        );
        var photoInput = addPathRow(
            "Folder foto:",
            defaultPhoto.exists ? defaultPhoto.fsName : "",
            function () { return Folder.selectDialog("Pilih folder foto pilihan"); }
        );
        var psdInput = addPathRow(
            "Folder PSD:",
            ROOT.fsName,
            function () { return Folder.selectDialog("Pilih folder PSD desain"); }
        );

        var formatGroup = win.add("group");
        formatGroup.orientation = "row";
        formatGroup.add("statictext", undefined, "Format foto:").preferredSize.width = 90;
        var photoFormat = formatGroup.add("dropdownlist", undefined, [
            "Semua Format (PNG, JPG, PSD)",
            "PNG saja",
            "JPG / JPEG saja"
        ]);
        photoFormat.selection = 0;

        var note = win.add("statictext", undefined,
            "PSD yang berhasil diisi akan dibiarkan terbuka dan belum disimpan.",
            { multiline: true }
        );
        note.preferredSize.width = 560;

        var buttons = win.add("group");
        buttons.alignment = "right";
        buttons.add("button", undefined, "Batal", { name: "cancel" });
        var run = buttons.add("button", undefined, "Masukkan Otomatis", { name: "ok" });

        run.onClick = function () {
            if (!dataInput.text || !new File(dataInput.text).exists) {
                alert("File data belum dipilih atau tidak ditemukan.");
                return;
            }
            if (!photoInput.text || !new Folder(photoInput.text).exists) {
                alert("Folder foto belum dipilih atau tidak ditemukan.");
                return;
            }
            if (!psdInput.text || !new Folder(psdInput.text).exists) {
                alert("Folder PSD belum dipilih atau tidak ditemukan.");
                return;
            }
            win.close(1);
        };

        if (win.show() !== 1) return null;
        var formatVal = "all";
        if (photoFormat.selection.index === 1) formatVal = "png";
        if (photoFormat.selection.index === 2) formatVal = "jpg";

        return {
            dataFile: new File(dataInput.text),
            photoFolder: new Folder(photoInput.text),
            psdFolder: new Folder(psdInput.text),
            reportJsonPath: Folder.temp.fsName + "/yb_process_report.json",
            photoFormat: formatVal,
            autoConfirm: false
        };
    }

    function makeProgress(maximum) {
        var win = new Window("palette", "ID Card Otomatis - Memproses PSD", undefined, { closeButton: false });
        win.orientation = "column";
        win.alignChildren = "fill";
        var status = win.add("statictext", undefined, "Proses 0/" + maximum);
        status.preferredSize.width = 240;
        win.show();
        return { win: win, status: status };
    }

    var progress = null;
    var log = [];
    var reportPages = [];
    var errors = [];
    var success = 0;
    var skipped = 0;
    var failed = 0;
    var missingLayers = [];
    var missingLayerSeen = {};
    var inputs = null;
    var revisionMode = false;
    var revisionFields = [];
    var startTime = new Date();

    try {
        inputs = chooseInputs();
        if (!inputs) return;
        revisionMode = inputs.operation === "revision";
        revisionFields = inputs.fields || [];

        var dataFile = inputs.dataFile;
        var photoFolder = inputs.photoFolder;
        var psdFolder = inputs.psdFolder;
        var psdFiles = listFiles(psdFolder, /\.(psd|psb)$/i);

        // Selalu kumpulkan semua format (PNG, PSD, JPG) agar prioritas bisa diterapkan per-nama.
        // Prioritas saat pencocokan: PNG > PSD > JPG (foto dengan kualitas terbaik utama).
        var pngFiles  = listFiles(photoFolder, /\.png$/i);
        var psdPhotoFiles = listFiles(photoFolder, /\.psd$/i);
        var jpgFiles  = listFiles(photoFolder, /\.jpe?g$/i);

        // Gabungan untuk keperluan log & statistik
        var photoFiles = pngFiles.concat(psdPhotoFiles).concat(jpgFiles);

        // Gabungan bucket dipakai untuk validasi jumlah pasangan semua format foto.
        var photoByName = buildFileBuckets(photoFiles);
        var pngByName   = buildFileBuckets(pngFiles);
        var psdPhotoByName = buildFileBuckets(psdPhotoFiles);
        var jpgByName   = buildFileBuckets(jpgFiles);

        if (!psdFiles.length) {
            var msgNoPsd = "Tidak ada file PSD di:\n" + psdFolder.fsName;
            errors.push(msgNoPsd);
            if (!inputs.autoConfirm) alert(msgNoPsd);
            return;
        }
        if (!revisionMode && !photoFiles.length) {
            var msgNoPhoto = "Tidak ada file foto (PNG/PSD/JPG) di folder foto. Proses akan tetap dilanjutkan untuk mengisi teks saja.";
            log.push(msgNoPhoto);
        }

        var preparedData = prepareDataFile(dataFile);
        var dataResult;
        try {
            dataResult = readDataFile(preparedData.file);
        } finally {
            if (preparedData.temporary) {
                try { preparedData.file.remove(); } catch (tempCleanupError) {}
            }
        }
        // photoByName tidak digunakan lagi secara langsung; prioritas ditangani via pngByName / psdPhotoByName / jpgByName.
        var psdByName = buildFileBuckets(psdFiles);

        // Nama yang sama tetap merupakan record terpisah. Pasangkan berdasarkan
        // urutan kemunculan pada data serta nomor/urutan nama file PSD dan foto.
        var repeatedWarning = [];
        if (dataResult.repeatedNames.length) {
            repeatedWarning.push(
                "NAMA BERULANG DI DATA:\n- " +
                dataResult.repeatedNames.join("\n\n- ")
            );
        }

        var allKeys = {}, wk;
        for (wk in dataResult.byName) allKeys[wk] = true;
        for (wk in photoByName) allKeys[wk] = true;
        for (wk in psdByName) allKeys[wk] = true;
        var countWarnings = [];
        for (wk in allKeys) {
            var dc = dataResult.byName[wk] ? dataResult.byName[wk].length : 0;
            var fc = photoByName[wk] ? photoByName[wk].length : 0;
            var pc = psdByName[wk] ? psdByName[wk].length : 0;
            if ((dc > 1 || fc > 1 || pc > 1) && !(dc === fc && fc === pc)) {
                countWarnings.push(wk + " → data " + dc + ", foto " + fc + ", PSD " + pc);
            }
        }
        if (countWarnings.length) {
            repeatedWarning.push(
                "JUMLAH PASANGAN TIDAK SEIMBANG:\n- " + countWarnings.join("\n- ")
            );
        }

        if (repeatedWarning.length && !inputs.autoConfirm) {
            var repeatedMessage =
                repeatedWarning.join("\n\n") +
                "\n\nSetiap baris tetap dianggap sebagai record berbeda. " +
                "Script akan memasangkan kemunculan ke-1, ke-2, dan seterusnya " +
                "mengikuti urutan data serta nomor/urutan file. " +
                "Pasangan yang jumlahnya kurang akan dilewati.\n\nLanjutkan proses?";
            if (!confirm(repeatedMessage)) return;
        }

        if (!inputs.autoConfirm) {
            var confirmation =
                "Data: " + decodeName(dataFile.name) + "\n" +
                (preparedData.excelInfo ? "Sumber: Excel, sheet pertama\n" : "Sumber: data teks / json\n") +
                "PSD: " + psdFiles.length + " file\n" +
                "Foto: " + photoFiles.length + " file\n\n" +
                "Script hanya memasukkan foto dan teks.\n" +
                "Dokumen akan dibiarkan terbuka dan belum disimpan.\n\nLanjutkan?";
            if (!confirm(confirmation)) return;
        }

        progress = makeProgress(psdFiles.length);
        log.push("IDCARD AUTO & BDATER PROCESS");
        log.push("Data: " + dataFile.fsName);
        log.push("Folder PSD: " + psdFolder.fsName);
        log.push("Folder foto: " + photoFolder.fsName);
        log.push("");

        var occurrenceByName = {};
        for (var p = 0; p < psdFiles.length; p++) {
            var psdFile = psdFiles[p];
            var key = normalizeName(psdFile.name);
            var occurrence = occurrenceByName[key] || 0;
            occurrenceByName[key] = occurrence + 1;
            var dataBucket = dataResult.byName[key] || [];
            var dataKey = key;
            var entry = dataBucket[occurrence];

            // Fallback: nama file PSD tak selalu persis sama dengan nama di data
            // (cukup nama depan, spasi beda, dsb.). Cari bucket data lain yang
            // kunci tanpa-spasinya saling beririsan.
            if (!entry && dataBucket.length === 0) {
                var noSpaceKey = key.replace(/\s+/g, "");
                if (noSpaceKey.length > 2) {
                    for (var dk in dataResult.byName) {
                        var dn = dk.replace(/\s+/g, "");
                        if (dn.length > 2 && (dn === noSpaceKey || dn.indexOf(noSpaceKey) === 0 || noSpaceKey.indexOf(dn) === 0)) {
                            var altBucket = dataResult.byName[dk];
                            if (altBucket[occurrence]) { entry = altBucket[occurrence]; dataKey = dk; break; }
                        }
                    }
                }
            }

            // Pasangan eksplisit dari BMachine (preview thumbnail / pilihan manual) diutamakan:
            // apa yang tampil di tabel itulah yang diproses. Bucket nama hanya fallback.
            var photo = null;
            var explicitPhotoPath = entry ? getEntryField(entry, "_MATCHED_PHOTO_PATH") : "";
            if (explicitPhotoPath) {
                var explicitFile = new File(explicitPhotoPath);
                if (explicitFile.exists) photo = pickBestPhoto(explicitFile);
            }
            if (!photo) {
                // Prioritas foto: PNG > PSD > JPG per kemunculan nama yang sama.
                // Foto biasanya bernama mengikuti data, jadi pakai dataKey.
                var pngBucket = pngByName[dataKey] || [];
                var psdBucket = psdPhotoByName[dataKey] || [];
                var jpgBucket = jpgByName[dataKey] || [];
                if (pngBucket[occurrence])        photo = pngBucket[occurrence];
                else if (psdBucket[occurrence])   photo = psdBucket[occurrence];
                else if (jpgBucket[occurrence])   photo = jpgBucket[occurrence];
            }

            progress.status.text = "Proses " + (p + 1) + "/" + psdFiles.length + " (" + decodeName(psdFile.name) + ")";
            progress.win.update();

            var pageReport = {
                psd: decodeName(psdFile.name),
                filled: 0,
                slots: 1,
                skippedNoData: false,
                items: []
            };

            if (!entry || (!revisionMode && !photo)) {
                var reasons = [];
                if (!entry) reasons.push("data tidak ditemukan");
                if (!photo) reasons.push("foto tidak ditemukan");
                var skipMsg = "LEWATI | " + decodeName(psdFile.name) + " | pasangan ke-" + (occurrence + 1) + " | " + reasons.join(", ");
                log.push(skipMsg);
                pageReport.skippedNoData = true;
                pageReport.items.push({
                    no: p + 1,
                    slot: "LAYER 1",
                    nama: decodeName(psdFile.name),
                    foto: "-",
                    ok: false,
                    photoOk: false,
                    textOk: false,
                    matchScore: 0,
                    info: reasons.join(", ")
                });
                reportPages.push(pageReport);
                skipped++;
                continue;
            }

            var doc = null;
            var completed = false;
            try {
                doc = app.open(psdFile);
                var layers = getTemplateLayers(doc);

                // Dynamic text mapping: setiap header data mencari Text Layer bernama sama di ISI n
                for (var h = 0; h < dataResult.headers.length; h++) {
                    var header = trimText(dataResult.headers[h]);
                    if (!header) continue;
                    if (revisionMode && !fieldSelected(header, revisionFields)) continue;
                    var textLayer = findTextLayerDeep(layers.isi, header);
                    if (textLayer) {
                        var val = getEntryField(entry, header);
                        textLayer.textItem.contents = val.replace(/\r\n/g, "\r").replace(/\n/g, "\r");
                    } else if (!missingLayerSeen[header.toLowerCase()]) {
                        missingLayerSeen[header.toLowerCase()] = true;
                        missingLayers.push(header);
                    }
                }

                if (!revisionMode && photo) replaceSmartObjectContents(doc, layers.foto, photo);
                completed = true;
                var okMsg = "OK     | " + decodeName(psdFile.name) + " | pasangan ke-" + (occurrence + 1) + " | " + decodeName(photo ? photo.name : "(tanpa foto)");
                log.push(okMsg);
                success++;

                pageReport.filled = 1;
                pageReport.items.push({
                    no: p + 1,
                    slot: "LAYER " + layers.number,
                    nama: decodeName(psdFile.name),
                    foto: decodeName(photo.name),
                    ok: true,
                    photoOk: true,
                    textOk: true,
                    matchScore: 100,
                    info: "Berhasil diisi & foto diganti"
                });
            } catch (itemError) {
                var errDesc = decodeName(psdFile.name) + " | " + itemError.message + " (baris " + itemError.line + ")";
                log.push("GAGAL  | " + errDesc);
                errors.push(errDesc);
                failed++;

                pageReport.items.push({
                    no: p + 1,
                    slot: "ERROR",
                    nama: decodeName(psdFile.name),
                    foto: photo ? decodeName(photo.name) : "-",
                    ok: false,
                    photoOk: false,
                    textOk: false,
                    matchScore: 0,
                    info: itemError.message
                });
            } finally {
                reportPages.push(pageReport);
                // Hanya tutup dokumen jika terjadi error/gagal; jika sukses, dokumen dibiarkan terbuka!
                if (doc && !completed) {
                    try { doc.close(SaveOptions.DONOTSAVECHANGES); }
                    catch (closeError) {}
                }
            }

            progress.win.update();
        }

        log.push("");
        log.push("Berhasil: " + success);
        log.push("Dilewati: " + skipped);
        log.push("Gagal: " + failed);
        if (missingLayers.length) {
            log.push("Header tanpa Text Layer yang sama: " + missingLayers.join(", "));
        }
        if (progress) progress.win.close();
        progress = null;

        var finishTime = new Date();
        var reportObj = {
            done: true,
            startedAt: startTime.toString(),
            finishedAt: finishTime.toString(),
            photosTotal: photoFiles.length,
            psdFilesTotal: psdFiles.length,
            rowsTotal: dataResult ? (dataResult.totalRows || psdFiles.length) : psdFiles.length,
            rowsProcessed: success,
            pages: reportPages,
            errors: errors
        };

        if (inputs.reportJsonPath) {
            try {
                writeTextFile(inputs.reportJsonPath, jsonStringify(reportObj));
            } catch (wrErr) {}
        }

        var finalMessage =
            "Selesai.\n\n" +
            "Berhasil: " + success + "\n" +
            "Dilewati: " + skipped + "\n" +
            "Gagal: " + failed + "\n\n" +
            "Semua PSD yang berhasil sudah diisi dan dibiarkan terbuka.\n" +
            "Belum ada dokumen yang disimpan.\n\n" +
            (log.length > 4 ? log.slice(4).join("\n") : "");

        if (inputs.autoConfirm) {
            $.global.ROBOT_IDCARD_LAST_RESULT = {
                success: success,
                skipped: skipped,
                failed: failed,
                log: log
            };
        } else {
            alert(finalMessage);
        }
    } catch (error) {
        if (progress) {
            try { progress.win.close(); } catch (progressError) {}
        }
        errors.push(error.message + " (baris " + error.line + ")");

        if (inputs && inputs.reportJsonPath) {
            try {
                var errReport = {
                    done: true,
                    startedAt: startTime.toString(),
                    finishedAt: new Date().toString(),
                    photosTotal: 0,
                    psdFilesTotal: 0,
                    rowsTotal: 0,
                    rowsProcessed: success,
                    pages: reportPages,
                    errors: errors
                };
                writeTextFile(inputs.reportJsonPath, jsonStringify(errReport));
            } catch (eRep) {}
        }

        if (inputs && inputs.autoConfirm) {
            $.global.ROBOT_IDCARD_LAST_RESULT = {
                success: success,
                skipped: skipped,
                failed: failed,
                error: error.message,
                line: error.line,
                log: log
            };
        } else {
            alert("Proses berhenti:\n" + error.message + "\nBaris: " + error.line);
        }
    } finally {
        app.displayDialogs = oldDialogs;
        app.preferences.rulerUnits = oldRulerUnits;
    }
})();
