/* Save + Cek-TeksPSD (gabungan, versi tanpa Whole Word)
   - Label & logika "Save Format" dipertahankan.
   - Panel opsional "Cek Teks PSD" sebelum proses save.
   - Case-sensitive default OFF.
   Photoshop 2020 (ExtendScript)
*/

app.bringToFront();

function main() {
    if (!app.documents.length) {
        alert("Tidak ada dokumen yang terbuka!");
        return;
    }

    // --- UI Dialog ---
    var dlg = new Window("dialog", "Save Options");
    dlg.orientation = "column";
    dlg.alignChildren = ["fill", "top"];
    dlg.spacing = 16;
    dlg.margins = 20;
    dlg.graphics.backgroundColor = dlg.graphics.newBrush(dlg.graphics.BrushType.SOLID_COLOR, [0.18,0.22,0.28,1]);

    // Title
    var header = dlg.add("statictext", undefined, "Save Action Photoshop");
    header.alignment = "center";
    header.graphics.font = ScriptUI.newFont(header.graphics.font.name, "BOLD", 18);
    header.graphics.foregroundColor = header.graphics.newPen(header.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);

    // Garis pemisah
    dlg.add("panel", undefined, undefined, {borderStyle:"sunken"}).preferredSize = [300,2];

    // ========== "Format File Utama" (LABEL & LOGIKA DIPERTAHANKAN) ==========
    var title1 = dlg.add("statictext", undefined, "Format File Utama :");
    title1.alignment = "center";
    title1.graphics.font = ScriptUI.newFont(title1.graphics.font.name, "BOLD", 12);
    var formatGroup = dlg.add("group");
    formatGroup.orientation = "row";
    formatGroup.alignChildren = ["center", "center"];
    var noneMainRadio = formatGroup.add("radiobutton", undefined, "TIDAK PAKE");
    var psdRadio = formatGroup.add("radiobutton", undefined, "PSD");
    var psbRadio = formatGroup.add("radiobutton", undefined, "PSB");
    var oriRadio  = formatGroup.add("radiobutton", undefined, "FA"); // ikut script asli
    // noneMainRadio.value = true;
    oriRadio.value = true; // Default FA

    psdRadio.onClick = function(){ psbRadio.value=false; noneMainRadio.value=false; oriRadio.value=false; };
    psbRadio.onClick = function(){ psdRadio.value=false; noneMainRadio.value=false; oriRadio.value=false; };
    noneMainRadio.onClick = function(){ psdRadio.value=false; psbRadio.value=false; oriRadio.value=false; };
    oriRadio.onClick = function(){ psdRadio.value=false; psbRadio.value=false; noneMainRadio.value=false; };

    // ========== "Format File Tambahan" (LABEL DIPERTAHANKAN) ==========
    var title2 = dlg.add("statictext", undefined, "Format File Tambahan:");
    title2.alignment = "center";
    title2.graphics.font = ScriptUI.newFont(title2.graphics.font.name, "BOLD", 12);
    var imgGroup = dlg.add("group");
    imgGroup.orientation = "row";
    imgGroup.alignChildren = ["center", "center"];
    var noneRadio = imgGroup.add("radiobutton", undefined, "TIDAK PAKE");
    var jpgRadio  = imgGroup.add("radiobutton", undefined, "JPG");
    var pngRadio  = imgGroup.add("radiobutton", undefined, "PNG");
    // noneRadio.value = true;
    jpgRadio.value = true; // Default JPG

    // Batch/Filter/Close (tetap sama)
    var optGroup = dlg.add("group");
    optGroup.orientation = "row";
    optGroup.alignChildren = ["center", "center"];
    var filterCheckbox = optGroup.add("checkbox", undefined, "NOISEWARE");
    var kelasCheckbox  = optGroup.add("checkbox", undefined, "KELAS");
    var batchCheckbox  = optGroup.add("checkbox", undefined, "BATCH");
    var closeCheckbox  = optGroup.add("checkbox", undefined, "CLOSE");
    filterCheckbox.value = true;
    kelasCheckbox.value  = false;
    batchCheckbox.value  = true;
    closeCheckbox.value  = true;

    // Garis pemisah
    dlg.add("panel", undefined, undefined, {borderStyle:"sunken"}).preferredSize = [300,2];

    // ========== Pilihan lokasi (LABEL DIPERTAHANKAN) ==========
    var title3 = dlg.add("statictext", undefined, "Pilih metode penyimpanan :");
    title3.alignment = "center";
    title3.graphics.font = ScriptUI.newFont(title3.graphics.font.name, "BOLD", 12);
    var btnGroup = dlg.add("group");
    btnGroup.orientation = "row";
    btnGroup.alignChildren = ["center", "center"];
    var sameBtn   = btnGroup.add("button", undefined, "SAVE");
    var otherBtn  = btnGroup.add("button", undefined, "SAVE TO BAHAN");
    var cancelBtn = btnGroup.add("button", undefined, "BATAL");

    function setButtonColor(btn, colorArr) {
        try {
            btn.graphics.backgroundColor = btn.graphics.newBrush(btn.graphics.BrushType.SOLID_COLOR, colorArr);
            btn.graphics.foregroundColor = btn.graphics.newPen(btn.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);
        } catch(e) {}
    }
    setButtonColor(sameBtn,  [0.18,0.55,0.34,1]);
    setButtonColor(otherBtn, [0.22,0.45,0.75,1]);
    setButtonColor(cancelBtn,[0.75,0.22,0.22,1]);

    // Garis pemisah
    dlg.add("panel", undefined, undefined, {borderStyle:"sunken"}).preferredSize = [300,2];

    // ========== Panel Tambahan: "Cek Teks PSD" (OPSIONAL) ==========
    var cekPanelTitle = dlg.add("statictext", undefined, "Cek Redaksi:");
    cekPanelTitle.alignment = "center";
    cekPanelTitle.graphics.font = ScriptUI.newFont(cekPanelTitle.graphics.font.name, "BOLD", 18);

    var cekGroup = dlg.add("group");
    cekGroup.orientation = "column";
    cekGroup.alignChildren = ["fill", "top"];

    var enableCheck = cekGroup.add("checkbox", undefined, "Cek Redaksi sebelum Save");
    enableCheck.value = true; // default aktif

    var wordsLabel = cekGroup.add("statictext", undefined, "Daftar kalimat terlarang (pisahkan koma / baris baru):");
    var wordsEdit  = cekGroup.add("edittext", [0,0,400,80],
        "TK DELAPAN MATA AIR\n" +
        "TK Delapan Mata Air\n" +
        "tk delapan mata air\n" +
        "TAAM AISYAH MIFTAHUL KHAER\n" +
        "JL. BIMA NO. 10 RT. 04 RW. 04  KEL. ARJUNA KEC. CICENDO\n" +
        "Jl. Sari Endah No. 7A Gegerkalong Hilir Bandung\n" +
        "Jl. Sari Endah No.7A, Sukarasa, Kec. Sukasari Kota Bandung, Jaw",
        {multiline:true, wantReturn:true}
    );

    // Garis pemisah
    dlg.add("panel", undefined, undefined, {borderStyle:"sunken"}).preferredSize = [300,2];

    // Catatan bawah
    var noteGroup = dlg.add("group");
    noteGroup.orientation = "row";
    noteGroup.alignment = "center";
    var computerName = $.getenv("COMPUTERNAME") || $.getenv("HOSTNAME") || "Komputer Anda";
    var noteText = noteGroup.add(
        "statictext",
        [0, 0, 400, 32],
        "NOTE : SELAMAT BEKERJA " + computerName + "!!\nUDAH DI NOISEWARE ",
        {multiline: true}
    );
    noteText.justify = "center";
    noteText.graphics.font = ScriptUI.newFont(noteText.graphics.font.name, "REGULAR", 9);
    noteText.graphics.foregroundColor = noteText.graphics.newPen(noteText.graphics.PenType.SOLID_COLOR, [0,1,0,1], 1);

    // ===== Helper dokumen sesuai Batch Mode =====
    function getTargetDocs(isBatch) {
        var arr = [];
        if (isBatch) {
            for (var i=0;i<app.documents.length;i++) arr.push(app.documents[i]);
        } else {
            arr.push(app.activeDocument);
        }
        return arr;
    }

    // ====== Cek Teks PSD (tanpa whole-word) ======
    function escReg(s) { return s.replace(/[-\/\\^$*+?.()|[\]{}]/g, "\\$&"); }

    // HANYA caseSensitive → flags 'g' atau 'gi'
    function buildRegex(wordsStr) {
        var tmp = String(wordsStr||"").replace(/\r/g,"\n").split(/[,|\n]/);
        var words = [];
        for (var i=0;i<tmp.length;i++){
            var s = tmp[i].replace(/^\s+|\s+$/g,"");
            if (s.length>0) words.push(s);
        }
        if (words.length===0) return null;

        var flags = "gi";
        var pat = "(";
        for (var j=0;j<words.length;j++){
            if (j>0) pat += "|";
            pat += escReg(words[j]);
        }
        pat += ")";
        return new RegExp(pat, flags);
    }

    function fullLayerPath(layer) {
        var parts = [layer.name];
        var p = layer.parent;
        while (p && p.typename === "LayerSet") {
            parts.unshift(p.name);
            p = p.parent;
        }
        return parts.join(">");
    }

    function layerContainsBlacklist(text, rx) {
        if (!text || !rx) return false;
        rx.lastIndex = 0;
        return rx.test(text);
    }

    function scanContainer(container, docName, rx, results) {
        var layers = container.layers;
        for (var i=0;i<layers.length;i++){
            var L = layers[i];
            if (L.typename === "ArtLayer" && L.kind === LayerKind.TEXT) {
                var isi = "";
                try { isi = L.textItem.contents; } catch(e){}
                if (layerContainsBlacklist(isi, rx)) {
                    try { L.color = LayerColor.RED; } catch(e){}
                    results.push(docName + " > " + fullLayerPath(L));
                }
            } else if (L.typename === "LayerSet") {
                scanContainer(L, docName, rx, results);
            }
        }
    }

    function runTextCheckOnDocs(docs, wordsStr) {
        var rx = buildRegex(wordsStr, false);
        if (!rx) {
            return { ok:false, hit:false, msg:"Daftar kalimat terlarang kosong.", results:[] };
        }

        var hasil = [];
        var originalDoc = app.activeDocument;
        for (var d=0; d<docs.length; d++){
            var doc = docs[d];
            app.activeDocument = doc;
            scanContainer(doc, doc.name, rx, hasil);
        }
        try { app.activeDocument = originalDoc; } catch(e){}

        if (hasil.length===0) {
            return { ok:true, hit:false, msg:"Cek Redaksi : Aman, madep lah pinter.", results:[] };
        } else {
            var pesan = "Layer terdeteksi kalimat terlarang:\n\n" +
                        hasil.join("\n") + "\n\nTotal: " + hasil.length + " layer";
            return { ok:true, hit:true, msg:pesan, results:hasil };
        }
    }

    // ===== Progress Bar Window (dari script save format) =====
    function showProgressBar(total) {
        var win = new Window("palette", "Progress", undefined, {closeButton: false});
        win.orientation = "column";
        win.alignChildren = ["fill", "center"];
        win.margins = 20;
        win.spacing = 10;

        var label = win.add("statictext", undefined, "Menyimpan file...");
        var bar = win.add("progressbar", undefined, 0, total);
        bar.preferredSize = [300, 20];
        var percent = win.add("statictext", undefined, "0%");

        win.updateProgress = function (val) {
            bar.value = val;
            percent.text = Math.round((val/total)*100) + "%";
            win.layout.layout(true);
        };

        win.show();
        return win;
    }

    // ===== Jalankan proses save (asli; tambah messageSuffix & opsi KELAS) =====
    function runSave(usePSD, usePSB, useJPG, usePNG, batchMode, closeAfter, sameLocation, useFilter, useOriFormat, useKelas, messageSuffix) {
        var docs;
        try {
            docs = batchMode ? app.documents : [app.activeDocument];
        } catch (e) {
            alert("Gagal mendapatkan dokumen: " + e);
            return;
        }

        var docList = [];
        for (var i = 0; i < docs.length; i++) docList.push(docs[i]);

        // Langsung proses save, tanpa dialog lokasi lain.
        for (var i = 0; i < docList.length; i++) {
            var doc = docList[i];
            try {
                app.activeDocument = doc;

                // Deteksi format asal jika FA
                var savePSD = usePSD, savePSB = usePSB;
                if (useOriFormat) {
                    var ext = "";
                    try { ext = doc.name.split('.').pop().toLowerCase(); } catch (e) {}
                    savePSD = (ext === "psd");
                    savePSB = (ext === "psb");
                }

                if (sameLocation) {
                    // LOGIKA LAMA: save di lokasi yang sama
                    if (!isDocumentHasPath(doc)) {
                        var fSame = Folder.selectDialog("Dokumen '" + doc.name + "' belum disimpan. Pilih folder tujuan untuk file ini:");
                        if (!fSame) continue;
                        saveToSpecifiedFolder(doc, savePSD, savePSB, useJPG, usePNG, fSame, useFilter);
                    } else {
                        saveToSameLocation(doc, savePSD, savePSB, useJPG, usePNG, useFilter);
                    }
                } else {
                    // LOGIKA BARU: tombol "Save di lokasi lain" → SAVE KE FOLDER BAHAN (dengan nama sesuai format tambahan, dan opsional nama kelas)
                    if (!isDocumentHasPath(doc)) {
                        alert("Dokumen '" + doc.name + "' belum disimpan, tidak bisa menentukan folder BAHAN.\nSimpan dulu dokumennya.");
                        continue;
                    }

                    var parentFolder = doc.fullName.parent;

                    // Tentukan nama folder BAHAN berdasarkan format tambahan dan opsi KELAS
                    var kelasSuffix = "";
                    if (useKelas) {
                        try {
                            var parentName = parentFolder.name;
                            if (parentName && parentName.length) {
                                kelasSuffix = " " + parentName;
                            }
                        } catch (eKelas) {}
                    }

                    var bahanFolderName = "BAHAN";
                    if (useJPG) {
                        bahanFolderName = "BAHAN" + kelasSuffix + " JPG";
                    } else if (usePNG) {
                        bahanFolderName = "BAHAN" + kelasSuffix + " PNG";
                    } else if (useKelas && kelasSuffix !== "") {
                        bahanFolderName = "BAHAN" + kelasSuffix;
                    }

                    // Jika opsi KELAS dipilih, simpan folder BAHAN di luar folder kelas (level parent)
                    var baseForBahan = parentFolder;
                    try {
                        if (useKelas && parentFolder.parent) {
                            baseForBahan = parentFolder.parent;
                        }
                    } catch (eBase) { baseForBahan = parentFolder; }

                    // Buat folder BAHAN
                    var bahanFolder;
                    if (useKelas) {
                        // Jika KELAS dicentang: struktur BAHAN (root) > BAHAN <KELAS> [JPG/PNG]
                        var bahanRoot = new Folder(baseForBahan + "/BAHAN");
                        if (!bahanRoot.exists) bahanRoot.create();

                        bahanFolder = new Folder(bahanRoot + "/" + bahanFolderName);
                        if (!bahanFolder.exists) bahanFolder.create();
                    } else {
                        // Jika KELAS tidak dicentang: buat folder satu tingkat saja
                        // Contoh: <baseForBahan>/BAHAN JPG  atau <baseForBahan>/BAHAN PNG
                        var singleName = "BAHAN";
                        if (useJPG) singleName = "BAHAN JPG";
                        else if (usePNG) singleName = "BAHAN PNG";
                        else singleName = bahanFolderName; // fallback

                        bahanFolder = new Folder(baseForBahan + "/" + singleName);
                        if (!bahanFolder.exists) bahanFolder.create();
                    }

                    saveToSpecifiedFolder(doc, savePSD, savePSB, useJPG, usePNG, bahanFolder, useFilter);
                }

                if (closeAfter) {
                    try { doc.close(SaveOptions.DONOTSAVECHANGES); } catch (eClose) {}
                }
            } catch (eDoc) {
                alert("Gagal memproses '" + doc.name + "': " + eDoc);
            }
            app.refresh();
            $.sleep(10);
        }

        var finalMsg = "Proses penyimpanan selesai!";
        if (messageSuffix && messageSuffix.length) {
            finalMsg += "\n" + messageSuffix;
        }
        alert(finalMsg);
    }

    function saveToSpecifiedFolder(doc, usePSD, usePSB, useJPG, usePNG, folder, useFilter) {
        var baseName = doc.name.replace(/\.[^\.]+$/, "");
        if (usePSD) {
            var psdFile = new File(folder + "/" + baseName + ".psd");
            var psdOptions = new PhotoshopSaveOptions();
            psdOptions.layers = true;
            doc.saveAs(psdFile, psdOptions, false, Extension.LOWERCASE);
        }
        if (usePSB) {
            var psbFile = new File(folder + "/" + baseName + ".psb");
            var desc = new ActionDescriptor();
            var desc2 = new ActionDescriptor();
            desc2.putBoolean(stringIDToTypeID("embedColorProfile"), true);
            desc2.putBoolean(stringIDToTypeID("layers"), true);
            desc2.putBoolean(stringIDToTypeID("maximizeCompatibility"), true);
            desc.putObject(charIDToTypeID("As  "), stringIDToTypeID("largeDocumentFormat"), desc2);
            desc.putPath(charIDToTypeID("In  "), psbFile);
            desc.putBoolean(charIDToTypeID("Cpy "), false);
            executeAction(charIDToTypeID("save"), desc, DialogModes.NO);
        }
        if (useJPG) {
            if (useFilter) {
                saveJPGWithFilter(doc, new File(folder + "/" + baseName + ".jpg"));
            } else {
                saveJPGNormal(doc, new File(folder + "/" + baseName + ".jpg"));
            }
        } else if (usePNG) {
            var pngFile = new File(folder + "/" + baseName + ".png");
            var pngOptions = new PNGSaveOptions();
            doc.saveAs(pngFile, pngOptions, true, Extension.LOWERCASE);
        }
    }

    function saveToSameLocation(doc, usePSD, usePSB, useJPG, usePNG, useFilter) {
        var docPath = doc.fullName.parent;
        var baseName = doc.name.replace(/\.[^\.]+$/, "");
        if (usePSD) {
            var psdFile = new File(docPath + "/" + baseName + ".psd");
            var psdOptions = new PhotoshopSaveOptions();
            psdOptions.layers = true;
            doc.saveAs(psdFile, psdOptions, false, Extension.LOWERCASE);
        }
        if (usePSB) {
            var psbFile = new File(docPath + "/" + baseName + ".psb");
            var desc = new ActionDescriptor();
            var desc2 = new ActionDescriptor();
            desc2.putBoolean(stringIDToTypeID("embedColorProfile"), true);
            desc2.putBoolean(stringIDToTypeID("layers"), true);
            desc2.putBoolean(stringIDToTypeID("maximizeCompatibility"), true);
            desc.putObject(charIDToTypeID("As  "), stringIDToTypeID("largeDocumentFormat"), desc2);
            desc.putPath(charIDToTypeID("In  "), psbFile);
            desc.putBoolean(charIDToTypeID("Cpy "), false);
            executeAction(charIDToTypeID("save"), desc, DialogModes.NO);
        }
        if (useJPG) {
            if (useFilter) {
                saveJPGWithFilter(doc, new File(docPath + "/" + baseName + ".jpg"));
            } else {
                saveJPGNormal(doc, new File(docPath + "/" + baseName + ".jpg"));
            }
        } else if (usePNG) {
            var pngFile = new File(docPath + "/" + baseName + ".png");
            var pngOptions = new PNGSaveOptions();
            doc.saveAs(pngFile, pngOptions, true, Extension.LOWERCASE);
        }
    }

    // Tambahkan variabel global untuk alert action
    var actionAlertShown = false;

    function saveJPGWithFilter(doc, jpgFile) {
        var originalDoc = app.activeDocument;
        var tempDoc = doc.duplicate();
        app.activeDocument = tempDoc;
        tempDoc.flatten();

        // Nama action dan set langsung di sini
        var actionName = "anti ramijud";
        var actionSet  = "starter pack";


        try {
            app.doAction(actionName, actionSet);
        } catch (e) {
            alert("Gagal menjalankan action \"" + actionName + "\" di set \"" + actionSet + "\".\nPastikan action dan set sudah ada di Photoshop.");
            tempDoc.close(SaveOptions.DONOTSAVECHANGES);
            app.activeDocument = originalDoc;
            return;
        }

        tempDoc.activeLayer.applyUnSharpMask(50, 2, 0);

        var jpgOptions = new JPEGSaveOptions();
        jpgOptions.quality = 12;
        tempDoc.saveAs(jpgFile, jpgOptions, true, Extension.LOWERCASE);
        tempDoc.close(SaveOptions.DONOTSAVECHANGES);
        app.activeDocument = originalDoc;
    }

    function saveJPGNormal(doc, jpgFile) {
        var originalDoc = app.activeDocument;
        var tempDoc = doc.duplicate();
        app.activeDocument = tempDoc;
        tempDoc.flatten();
        var jpgOptions = new JPEGSaveOptions();
        jpgOptions.quality = 12;
        tempDoc.saveAs(jpgFile, jpgOptions, true, Extension.LOWERCASE);
        tempDoc.close(SaveOptions.DONOTSAVECHANGES);
        app.activeDocument = originalDoc;
    }

    function isDocumentHasPath(doc) {
        try { var p = doc.fullName; return true; } catch (e) { return false; }
    }

    // ====== Wiring tombol ======
    function handleSaveClick(sameLocationFlag) {
        // 1) Dokumen target sesuai Batch Mode
        var docs = getTargetDocs(batchCheckbox.value);

        // 2) Jika cek teks aktif → jalankan cek dulu
        if (enableCheck.value) {
            var res = runTextCheckOnDocs(docs, wordsEdit.text, false);
            if (!res.ok) {
                alert(res.msg);
                return; // tidak lanjut save
            }
            if (res.hit) {
                // Ada kata blacklist → tampilkan pesan & BATAL save (sesuai permintaan)
                alert(res.msg);
                return;
            }
            // Aman → lanjut save, dan kirim suffix pesan aman
            var suffix = "Cek Redaksi: Aman, anjeun pinter (tak ada kalimat terlarang).";
            runSave(
                psdRadio.value,
                psbRadio.value,
                jpgRadio.value,
                pngRadio.value,
                batchCheckbox.value,
                closeCheckbox.value,
                sameLocationFlag,
                filterCheckbox.value,
                oriRadio.value,
                kelasCheckbox.value,
                suffix
            );
            return;
        }

        // 3) Jika cek teks non-aktif → langsung save
        runSave(
            psdRadio.value,
            psbRadio.value,
            jpgRadio.value,
            pngRadio.value,
            batchCheckbox.value,
            closeCheckbox.value,
            sameLocationFlag,
            filterCheckbox.value,
            oriRadio.value,
            kelasCheckbox.value,
            "" // tanpa suffix
        );
    }

    sameBtn.onClick  = function(){ dlg.close(); handleSaveClick(true);  };
    otherBtn.onClick = function(){ dlg.close(); handleSaveClick(false); };
    cancelBtn.onClick= function(){ dlg.close(); };

    dlg.center();
    dlg.show();
}

main();
