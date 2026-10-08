// @target photoshop

// Map to track unchecked artboards already exported per class folder (must be
// declared before main() is invoked below, otherwise it is undefined at runtime).
var processedUnchecked = {};

/*
    unified_save_master.jsx
    Fitur: Dialog pilihan mode untuk simpan master + export format lain.
    Update: Added Custom Folder UI & Window Persistence.
*/

function loadSettings() {
    var settingsFile = new File(Folder.userData + "/save_master_settings.json");
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

function saveSettings(x, y) { var settings=loadSettings(); settings.x=x; settings.y=y; saveSettingsData(settings); }
function saveSettingsData(settings) {
    var file=new File(Folder.userData+"/save_master_settings.json");
    try { file.open("w"); file.write('{"x":'+(settings.x||-1)+',"y":'+(settings.y||-1)+',"advancedX":'+(settings.advancedX||-1)+',"advancedY":'+(settings.advancedY||-1)+',"selX":'+(settings.selX||-1)+',"selY":'+(settings.selY||-1)+'}'); file.close(); }
    catch(e) { try { file.close(); } catch(ex) {} }
}


function loadAdvancedPresets(){var f=new File(Folder.userData+"/save_master_presets.json");if(!f.exists)return[];try{f.open("r");var d=eval("("+f.read()+")");f.close();return d&&d.presets&&d.presets.constructor===Array?d.presets:[];}catch(e){try{f.close();}catch(x){}return[];}}
function saveAdvancedPresets(p){var f=new File(Folder.userData+"/save_master_presets.json");try{f.open("w");f.write(({presets:p}).toSource());f.close();return true;}catch(e){try{f.close();}catch(x){}return false;}}
function isBatchEnabled(value){return value==="all"||value===true||String(value).toLowerCase()==="true"||String(value)==="1"||String(value).toLowerCase()==="on";}
function showAdvancedMode(){
 var presets=loadAdvancedPresets(),pos=loadSettings(),dlg=new Window("dialog","Save Master - Advanced");dlg.orientation="column";dlg.alignChildren=["fill","top"];dlg.spacing=7;dlg.margins=12;
 var panel=dlg.add("panel",undefined,"Saved presets - click button to run");panel.orientation="column";panel.alignChildren=["fill","top"];panel.preferredSize=[350,140];var area=panel.add("group");area.orientation="column";area.alignChildren=["fill","top"];
 var name=dlg.add("edittext",undefined,"");var row=dlg.add("group");row.orientation="row";var master=row.add("checkbox",undefined,"Save PSD");master.value=true;var jpg=row.add("checkbox",undefined,"JPG");var png=row.add("checkbox",undefined,"PNG");var action=dlg.add("checkbox",undefined,"Run anti ramijud action");action.value=true;var keep=dlg.add("checkbox",undefined,"Keep source open");keep.value=true;var batch=dlg.add("checkbox",undefined,"Batch: all open documents");batch.value=false;
 var pr=dlg.add("group");pr.orientation="row";pr.add("statictext",undefined,"Export folder (blank = source)");var path=pr.add("edittext",undefined,"");path.preferredSize.width=145;var browse=pr.add("button",undefined,"Browse");var buttons=dlg.add("group");buttons.orientation="row";buttons.alignment="center";var save=buttons.add("button",undefined,"Save / Edit");var run=buttons.add("button",undefined,"Run Current");var cancel=buttons.add("button",undefined,"Cancel");
 function conf(){return{name:name.text,saveMaster:master.value,exportJpg:jpg.value,exportPng:png.value,processAction:action.value,keepOpen:keep.value,batch:batch.value,path:path.text};}
 function execute(c){if(!c.saveMaster&&!c.exportJpg&&!c.exportPng){alert("Aktifkan minimal satu proses.");return;}var f=c.path.replace(/^\s+|\s+$/g,"");if(f&&!new Folder(f).exists){alert("Folder export tidak ditemukan.");return;}dlg.close(1);runAdvancedMode({saveMaster:c.saveMaster,exportJpg:c.exportJpg,exportPng:c.exportPng,processAction:c.processAction,keepOpen:c.keepOpen,batch:isBatchEnabled(c.batch),path:f});}
 function populate(){while(area.children.length)area.remove(area.children[0]);var r=null;for(var i=0;i<presets.length;i++){if(i%3===0){r=area.add("group");r.orientation="row";}var item=r.add("group");item.orientation="row";item.spacing=1;var b=item.add("button",undefined,presets[i].name);b.preferredSize=[88,25];b.idx=i;b.onClick=function(){var c=presets[this.idx];if(typeof c.batch==="undefined")c.batch=false;execute(c);};var x=item.add("button",undefined,"X");x.preferredSize=[18,25];x.idx=i;x.helpTip="Hapus preset";x.onClick=function(){var idx=this.idx;if(!confirm("Hapus preset '"+presets[idx].name+"'?"))return;presets.splice(idx,1);if(!saveAdvancedPresets(presets)){alert("Gagal menghapus preset.");return;}populate();};}if(!presets.length)area.add("statictext",undefined,"Belum ada preset tersimpan.");area.layout.layout(true);panel.layout.layout(true);dlg.layout.layout(true);dlg.update();}
 populate();browse.onClick=function(){var f=Folder.selectDialog("Pilih folder export");if(f)path.text=decodeURI(f.fsName);};function savePreset(){var c=conf(),n=c.name.replace(/^\s+|\s+$/g,"");if(!n){alert("Isi nama preset.");return;}c.name=n;var i=-1;for(var j=0;j<presets.length;j++)if(presets[j].name===n)i=j;if(i<0)presets.push(c);else presets[i]=c;if(!saveAdvancedPresets(presets)){alert("Gagal menyimpan preset.");return;}populate();area.layout.layout(true);panel.layout.layout(true);dlg.layout.layout(true);dlg.layout.resize();dlg.update();}save.onClick=savePreset; run.onClick=function(){execute(conf());};cancel.onClick=function(){dlg.close(0);};dlg.onClose=function(){var s=loadSettings();s.advancedX=dlg.location.x;s.advancedY=dlg.location.y;saveSettingsData(s);return true;};if(pos.advancedX>0&&pos.advancedY>0)dlg.location=[pos.advancedX,pos.advancedY];else dlg.center();dlg.show();
}
function runAdvancedMode(c){
 var docNames=[];var batchMode=isBatchEnabled(c.batch);
 if(batchMode){for(var i=0;i<app.documents.length;i++)docNames.push(app.documents[i].name);}
 else if(app.activeDocument)docNames.push(app.activeDocument.name);
 var ok=[],fail=[],stop=false,requested=false;
 if(!docNames.length){alert("Tidak ada dokumen untuk diproses.");return;}
 var w=new Window("palette","Advanced Save Progress");w.orientation="column";w.alignChildren=["fill","top"];w.margins=10;
 var status=w.add("statictext",undefined,(batchMode?"BATCH ON - ":"DOKUMEN AKTIF - ")+docNames.length+" dokumen");
 var cancel=w.add("button",undefined,"Batal setelah langkah berjalan");
 cancel.onClick=function(){requested=true;status.text="Pembatalan diminta; menunggu langkah aktif selesai...";w.update();};
 w.show();
 try{
  for(var n=0;n<docNames.length;n++){
   if(requested){stop=true;break;}
   var docName=docNames[n],base=docName.replace(/\.[^.]+$/,""),dup=null;
   status.text="Memproses "+(n+1)+"/"+docNames.length+": "+docName;w.update();
   try{
    var d=null;
    for(var k=0;k<app.documents.length;k++){if(app.documents[k].name===docName){d=app.documents[k];break;}}
    if(!d){fail.push(docName+" (dokumen tidak ditemukan/sudah ditutup)");continue;}
    app.activeDocument=d;
    var m=docName.match(/\.([^.]+)$/),ext=m?m[1].toLowerCase():"";
    if(c.saveMaster){
     if((ext==="jpg"||ext==="jpeg"||ext==="png")&&d.path){
      var f=new File(d.path.fsName+"/"+base+".psd"),o=new PhotoshopSaveOptions();
      o.embedColorProfile=true;o.layers=true;o.maximizeCompatibility=true;
      d.saveAs(f,o,true,Extension.LOWERCASE);
     }else if(!d.path){
      var sf=File.saveDialog("Simpan master PSD","Photoshop:*.psd");
      if(!sf)throw new Error("Save dibatalkan");
      if(!/\.psd$/i.test(sf.name))sf=new File(sf.fsName+".psd");
      var so=new PhotoshopSaveOptions();so.embedColorProfile=true;so.layers=true;so.maximizeCompatibility=true;
      d.saveAs(sf,so,true,Extension.LOWERCASE);
     }else d.save();
    }
    var folder=c.path?new Folder(c.path):d.path;
    if((c.exportJpg||c.exportPng)&&!folder)throw new Error("Pilih folder export.");
    if(c.exportJpg||c.exportPng){
     dup=d.duplicate(base+"_advanced_export");app.activeDocument=dup;dup.flatten();
     if(!requested&&c.processAction)try{app.doAction("anti ramijud","starter pack");}catch(ex){}
     if(!requested&&c.exportJpg)saveJPG(dup,folder.fsName+"/"+base+".jpg",12);
     if(!requested&&c.exportPng)savePNG(dup,folder.fsName+"/"+base+".png");
     dup.close(SaveOptions.DONOTSAVECHANGES);dup=null;app.activeDocument=d;
    }
    if(requested){stop=true;break;}
    if(!c.keepOpen){d.close(SaveOptions.DONOTSAVECHANGES);}
    ok.push(docName);
   }catch(e){
    if(e.number===8007||(e.message&&e.message.toLowerCase().indexOf("cancel")>=0)){stop=true;break;}
    fail.push(docName+" ("+e.message+")");
   }finally{
    if(dup)try{dup.close(SaveOptions.DONOTSAVECHANGES);}catch(ex2){}
   }
  }
 }finally{try{w.close();}catch(ex4){}}
 var msg="Sukses: "+ok.length+"\n"+ok.join("\n")+"\n\nGagal: "+fail.length+"\n"+fail.join("\n");
 if(stop||requested)msg+="\n\n--- DIBATALKAN; HASIL PARSIAL ---";
 showScrollableAlert("Laporan Advanced Save",msg);
}

function main() {
    // === KONFIGURASI ===
    var JPG_QUALITY = 12;

    // === MAIN LOGIC ===
    if (app.documents.length == 0) {
        alert("Tidak ada dokumen terbuka di Photoshop.");
        return;
    }

    var settings = loadSettings();

    // ========== File Pengaturan Redaksi ==========
    var settingFile = new File(Folder.userData + "/bmachine_fendi_redaksi.txt");
    var defaultText = "TK DELAPAN MATA AIR\nTAAM AISYAH MIFTAHUL KHAER\nJL. BIMA NO. 10 RT. 04 RW. 04  KEL. ARJUNA KEC. CICENDO\nJl. Sari Endah No. 7A Gegerkalong Hilir Bandung\nJl. Sari Endah No.7A, Sukarasa, Kec. Sukasari Kota Bandung, Jaw";
    var currentRedaksi = defaultText;
    if (settingFile.exists) {
        try {
            settingFile.open("r");
            currentRedaksi = settingFile.read();
            settingFile.close();
        } catch(e){}
    } else {
        try {
            settingFile.open("w");
            settingFile.write(defaultText);
            settingFile.close();
        } catch(e){}
    }

    // === DIALOG UTAMA ===
    var dlg = new Window("dialog", "Save Master Options", undefined, { borderless: true }); // Borderless removed white title bar
    dlg.orientation = "column";
    dlg.alignChildren = ["fill", "top"];
    dlg.spacing = 15;
    dlg.margins = 20;

    // --- PANEL: STANDARD OUTPUT (3 Columns) ---
    var pnlStd = dlg.add("panel", undefined, "Standard Output (Same Folder)");
    pnlStd.orientation = "row";
    pnlStd.alignChildren = ["fill", "top"];
    pnlStd.spacing = 20;
    pnlStd.margins = 15;

    // Col 1: JPG
    var col1 = pnlStd.add("group");
    col1.orientation = "column";
    col1.add("statictext", undefined, "--- JPG ---");
    var btnJpg = col1.add("button", undefined, "JPG - TUTUP");
    btnJpg.preferredSize.width = 140;

    var btnJpgOpen = col1.add("button", undefined, "JPG - TETAP");
    btnJpgOpen.preferredSize.width = 140;

    var btnSelectedJpg = col1.add("button", undefined, "TERPILIH");
    btnSelectedJpg.preferredSize.width = 140;

    // Separator small
    var sepJpg = col1.add("panel");
    sepJpg.alignment = "fill";
    sepJpg.preferredSize.height = 1;

    // --- SMART SAVE BUTTON ---
    var btnSmartSave = col1.add("button", undefined, "SAVE ORIGINAL (AUTO)");
    btnSmartSave.preferredSize.width = 140;
    btnSmartSave.helpTip = "Otomatis update JPG/PNG yang sudah ada. Jika tidak ada, hanya Save PSD.";

    // Col 2: PNG & PAS FOTO (Merged)
    var col2 = pnlStd.add("group");
    col2.orientation = "column";
    col2.add("statictext", undefined, "--- PNG & PAS FOTO ---");

    // Pas Foto
    var btnPas2x3 = col2.add("button", undefined, "PAS FOTO (2x3)");
    btnPas2x3.preferredSize.width = 140;
    var btnPas4x6 = col2.add("button", undefined, "PAS FOTO (4x6)");
    btnPas4x6.preferredSize.width = 140;
    var btnPasKombi = col2.add("button", undefined, "PAS FOTO (KOMBI)");
    btnPasKombi.preferredSize.width = 140;

    // Separator small
    var sep = col2.add("panel");
    sep.alignment = "fill";
    sep.preferredSize.height = 1;

    // PNG
    var btnPng = col2.add("button", undefined, "PNG Standard");
    btnPng.preferredSize.width = 140;

    var artboardsInfoCurrent = getArtboardsData();
    var artboardCheckboxes = [];
    var btnArtJpg; // Deklarasikan di luar scope agar bisa dicek di event

    if (artboardsInfoCurrent.length > 0) {
        // Col 3: ARTBOARD
        var col3 = pnlStd.add("group");
        col3.orientation = "column";
        col3.add("statictext", undefined, "--- ARTBOARD ---");

        for (var i = 0; i < artboardsInfoCurrent.length; i++) {
            var abName = artboardsInfoCurrent[i].name;
            var chk = col3.add("checkbox", undefined, abName);
            chk.value = false; // Tidak ada yang dicentang secara default
            chk.alignment = "left"; // Meratakan kotak ceklis ke kiri
            artboardCheckboxes.push({ name: abName, checkbox: chk });
        }

        btnArtJpg = col3.add("button", undefined, "PROSES ART");
        btnArtJpg.preferredSize.width = 140;
        btnArtJpg.helpTip = "Export Artboard ke JPG untuk semua file (Batch)";
    }

    // --- BOTTOM ---
    var grpBottom = dlg.add("group");
    grpBottom.orientation = "row";
    grpBottom.alignChildren = ["center", "center"];
    grpBottom.alignment = "center";
    
    var btnSettingRedaksi = grpBottom.add("button", undefined, "Pengaturan Redaksi");
    var btnAdvanced = grpBottom.add("button", undefined, "Advanced");
    var btnCancel = grpBottom.add("button", undefined, "Cancel");

    btnSettingRedaksi.onClick = function() {
        var redaksiDlg = new Window("dialog", "Pengaturan Redaksi");
        redaksiDlg.orientation = "column";
        redaksiDlg.alignChildren = ["fill", "top"];
        redaksiDlg.spacing = 10;
        redaksiDlg.margins = 15;
        
        redaksiDlg.add("statictext", undefined, "Daftar Teks Terlarang (Otomatis Aktif):");
        var wordsEdit = redaksiDlg.add("edittext", [0,0,400,150], currentRedaksi, {multiline:true, wantReturn:true});
        
        var grpRedaksiBtn = redaksiDlg.add("group");
        grpRedaksiBtn.alignment = "center";
        var btnSaveRedaksi = grpRedaksiBtn.add("button", undefined, "Simpan");
        var btnCloseRedaksi = grpRedaksiBtn.add("button", undefined, "Batal");
        
        btnSaveRedaksi.onClick = function() {
            try {
                settingFile.open("w");
                settingFile.write(wordsEdit.text);
                settingFile.close();
                currentRedaksi = wordsEdit.text;
                alert("Teks redaksi berhasil disimpan!");
                redaksiDlg.close();
            } catch(e) {
                alert("Gagal menyimpan teks redaksi: " + e.message);
            }
        };
        btnCloseRedaksi.onClick = function() { redaksiDlg.close(); };
        redaksiDlg.show();
    };

    // --- EVENTS ---
    btnJpg.onClick = function () { dlg.close(1); };
    btnJpgOpen.onClick = function () { dlg.close(5); };
    btnSelectedJpg.onClick = function () { dlg.close(100); }; // Code 100 for Selected JPG
    btnPng.onClick = function () { dlg.close(2); };

    // Pas Foto (Kode 301 dihapus karena redundant dengan JPG Biasa)
    btnPas2x3.onClick = function () { dlg.close(302); };
    btnPas4x6.onClick = function () { dlg.close(303); };
    btnPasKombi.onClick = function () { dlg.close(304); };
    
    if (btnArtJpg) {
        btnArtJpg.onClick = function () { dlg.close(401); };
    }

    btnSmartSave.onClick = function () { dlg.close(99); }; // Code 99 for Smart Save

    btnAdvanced.onClick = function () { dlg.close(-1); };

    btnCancel.onClick = function () { dlg.close(0); };

    // Restore Location
    if (settings.x > 0 && settings.y > 0) {
        dlg.location = [settings.x, settings.y];
    } else {
        dlg.center();
    }

    var choice = dlg.show();

    // Save Location
    saveSettings(dlg.location.x, dlg.location.y);

    if (choice == 0) return; // Cancelled
    if (choice == -1) { showAdvancedMode(); return; }

    // === PROCESSING LOGIC Setup ===

    // --- SPECIAL MODE: SELECTED (100) ---
    if (choice == 100) {
        var selDlg = new Window("dialog", "Save Terpilih - Pilih Cara & Dokumen");
        selDlg.orientation = "column";
        selDlg.alignChildren = ["fill", "top"];
        selDlg.spacing = 8;
        selDlg.margins = 12;
        var selPos = loadSettings();
        selDlg.onClose = function () { var s = loadSettings(); s.selX = selDlg.location.x; s.selY = selDlg.location.y; saveSettingsData(s); return true; };
        if (selPos.selX > 0 && selPos.selY > 0) selDlg.location = [selPos.selX, selPos.selY]; else selDlg.center();

        var mainRow = selDlg.add("group");
        mainRow.orientation = "row";
        mainRow.alignChildren = ["top", "top"];
        mainRow.spacing = 12;

        // === KOLOM KIRI: TOMBOL CARA SAVE ===
        var leftCol = mainRow.add("group");
        leftCol.orientation = "column";
        leftCol.alignChildren = ["fill", "top"];
        leftCol.spacing = 5;
        leftCol.add("statictext", undefined, "--- NORMAL SAVE ---");

        var btnMethodJpg = leftCol.add("button", undefined, "JPG");
        var btnMethodPng = leftCol.add("button", undefined, "PNG");

        var sepMethod = leftCol.add("panel");
        sepMethod.alignment = "fill";
        sepMethod.preferredSize.height = 1;

        leftCol.add("statictext", undefined, "--- PAS FOTO ---");
        var btnMethodPas2x3 = leftCol.add("button", undefined, "PAS FOTO (2x3)");
        var btnMethodPas4x6 = leftCol.add("button", undefined, "PAS FOTO (4x6)");
        var btnMethodPasKombi = leftCol.add("button", undefined, "PAS FOTO (KOMBI)");

        var leftBtnW = 150;
        btnMethodJpg.preferredSize.width = leftBtnW;
        btnMethodPng.preferredSize.width = leftBtnW;
        btnMethodPas2x3.preferredSize.width = leftBtnW;
        btnMethodPas4x6.preferredSize.width = leftBtnW;
        btnMethodPasKombi.preferredSize.width = leftBtnW;
        btnMethodPas2x3.helpTip = "Simpan JPG + crop 2x3 cm ke subfolder /2x3";
        btnMethodPas4x6.helpTip = "Simpan JPG + crop 4x6 cm ke subfolder /4x6";
        btnMethodPasKombi.helpTip = "Simpan JPG + crop 2x3 dan 4x6 cm";

        // === KOLOM KANAN: LIST DOKUMEN ===
        var rightCol = mainRow.add("group");
        rightCol.orientation = "column";
        rightCol.alignChildren = ["fill", "top"];
        rightCol.spacing = 5;
        rightCol.add("statictext", undefined, "--- DOKUMEN (Ctrl untuk pilih banyak) ---");

        var lstDocs = rightCol.add("listbox", undefined, [], { multiselect: true });
        lstDocs.preferredSize = [330, 240];

        var allDocs = [];
        for (var i = 0; i < app.documents.length; i++) {
            allDocs.push(app.documents[i]);
            lstDocs.add("item", app.documents[i].name);
        }
        for (var k = 0; k < lstDocs.items.length; k++) {
            lstDocs.items[k].selected = true;
        }

        var selRow = rightCol.add("group");
        selRow.orientation = "row";
        var btnSelectAll = selRow.add("button", undefined, "Pilih Semua");
        var btnSelectNone = selRow.add("button", undefined, "Kosongkan");
        btnSelectAll.preferredSize.width = 120;
        btnSelectNone.preferredSize.width = 120;

        btnSelectAll.onClick = function () {
            for (var k = 0; k < lstDocs.items.length; k++) lstDocs.items[k].selected = true;
        };
        btnSelectNone.onClick = function () {
            for (var k = 0; k < lstDocs.items.length; k++) lstDocs.items[k].selected = false;
        };

        var btnSelCancel = selDlg.add("button", undefined, "CANCEL");
        btnSelCancel.alignment = "center";

        btnMethodJpg.onClick = function () { selDlg.close(1); };
        btnMethodPng.onClick = function () { selDlg.close(2); };
        btnMethodPas2x3.onClick = function () { selDlg.close(3); };
        btnMethodPas4x6.onClick = function () { selDlg.close(4); };
        btnMethodPasKombi.onClick = function () { selDlg.close(5); };
        btnSelCancel.onClick = function () { selDlg.close(0); };

        var selChoice = selDlg.show();
        if (selChoice >= 1 && selChoice <= 5) {
            // Kumpulkan dokumen terpilih
            var selectedIndices = [];
            for (var k = 0; k < lstDocs.items.length; k++) {
                if (lstDocs.items[k].selected) selectedIndices.push(k);
            }
            if (selectedIndices.length == 0) return;

            var docsToProcess = [];
            for (var m = 0; m < selectedIndices.length; m++) {
                docsToProcess.push(allDocs[selectedIndices[m]]);
            }

            // === CEK REDAKSI ===
            var resCheck = runTextCheckOnDocs(docsToProcess, currentRedaksi);
            if (!resCheck.ok) {
                showScrollableAlert("Peringatan Cek Redaksi", resCheck.msg);
                return;
            }
            if (resCheck.hit) {
                showScrollableAlert("Peringatan Cek Redaksi", resCheck.msg + "\n\nProses Save Dibatalkan.");
                return;
            }

            var successList = [];
            var failList = [];
            var isPasMode = (selChoice >= 3);

            for (var d = 0; d < docsToProcess.length; d++) {
                var docName = docsToProcess[d] ? docsToProcess[d].name : "Unknown Document";
                try {
                    var doc = docsToProcess[d];
                    app.activeDocument = doc;

                    if (!doc.path) {
                        failList.push(docName + " (Belum disave/tidak ada path)");
                        continue;
                    }

                    var baseName = doc.name.replace(/\.[^\.]+$/, "");
                    var docPath = doc.path;

                    // 1. Save PSD master
                    doc.save();

                    // 2. Export utama (JPG untuk mode JPG & Pas Foto, PNG untuk mode PNG)
                    if (selChoice == 1 || isPasMode) {
                        var dupDoc = doc.duplicate(baseName + "_temp");
                        app.activeDocument = dupDoc;
                        dupDoc.artLayers.add();
                        dupDoc.flatten();
                        try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                        saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                        dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    } else if (selChoice == 2) {
                        var dupDoc = doc.duplicate(baseName + "_forPNG");
                        app.activeDocument = dupDoc;
                        dupDoc.artLayers.add();
                        executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);
                        try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                        savePNG(dupDoc, docPath + "/" + baseName + ".png");
                        dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    }

                    // 3. Crop pas foto (2x3 / 4x6)
                    if (isPasMode) {
                        app.activeDocument = doc;
                        if (selChoice == 3 || selChoice == 5) {
                            var folder2x3 = createFolderIfNotExist(docPath + "/2x3");
                            createCroppedVersion(doc, folder2x3, 2, 3, JPG_QUALITY);
                        }
                        if (selChoice == 4 || selChoice == 5) {
                            var folder4x6 = createFolderIfNotExist(docPath + "/4x6");
                            createCroppedVersion(doc, folder4x6, 4, 6, JPG_QUALITY);
                        }
                    }

                    // 4. Tutup dokumen asli
                    app.activeDocument = doc;
                    doc.close(SaveOptions.DONOTSAVECHANGES);
                    successList.push(docName);

                } catch (e) {
                    failList.push(docName + " (Error: " + e.message + ")");
                }
            }

            var msg = "Sukses: " + successList.length + "\n";
            if (successList.length > 0) msg += successList.join("\n") + "\n\n";
            msg += "Gagal: " + failList.length + "\n";
            if (failList.length > 0) msg += failList.join("\n");

            showScrollableAlert("Laporan Proses Dokumen Terpilih", msg);
        }
        return; // Exit main
    }

    // === JALANKAN CEK REDAKSI OTOMATIS UNTUK SEMUA DOKUMEN (KECUALI SAVE ORIGINAL) ===
    if (choice != 99) {
        var allOpenDocs = [];
        for (var i = 0; i < app.documents.length; i++) {
            allOpenDocs.push(app.documents[i]);
        }
        
        var resCheck = runTextCheckOnDocs(allOpenDocs, currentRedaksi);
        if (!resCheck.ok) {
            showScrollableAlert("Peringatan Cek Redaksi", resCheck.msg);
            return;
        }
        if (resCheck.hit) {
            showScrollableAlert("Peringatan Cek Redaksi", resCheck.msg + "\n\nProses Save Dibatalkan.");
            return;
        }
    }

    // --- SPECIAL MODE: ARTBOARD (401) ---
    if (choice == 401) {
        var isPng = false;
        
        var docs = [];
        for (var i = 0; i < app.documents.length; i++) {
            docs.push(app.documents[i]);
        }
        
        var totalSuccess = 0;
        var totalFail = 0;
        var allDetails = [];

        var repeatByName = {};
        for (var cb = 0; cb < artboardCheckboxes.length; cb++) {
            var nName = normalizeArtboardName(artboardCheckboxes[cb].name);
            repeatByName[nName] = artboardCheckboxes[cb].checkbox.value;
        }
        var exportSchedules = [];

        function normalizeArtboardName(name) {
            return String(name || "").replace(/^\s+|\s+$/g, "").toUpperCase();
        }

        // Build schedule: SEMUA artboard di setiap dokumen ikut diexport
        // (agar tiap folder kelas lengkap). Checkbox hanya menentukan
        // apakah artboard disimpan di root folder kelas (repeat) atau subfolder.
        for (var i = 0; i < docs.length; i++) {
            var schedule = [];
            var doc = docs[i];
            try {
                app.activeDocument = doc;
                var artboardsInfo = getArtboardsData();
                for (var scheduleIdx = 0; scheduleIdx < artboardsInfo.length; scheduleIdx++) {
                    schedule.push(scheduleIdx);
                }
            } catch (e) {}
            exportSchedules.push(schedule);
        }

        // Execute schedule
        for (var i = 0; i < docs.length; i++) {
            var doc = docs[i];
            var currentSchedule = exportSchedules[i];
            try {
                app.activeDocument = doc;
                
                if (!doc.path) {
                    allDetails.push(doc.name + " (Gagal: Belum disave/tidak ada path)");
                    totalFail++;
                    continue;
                }
                
                // Simpan PSD original dulu
                doc.save();
                
                var res = exportArtboards(doc, isPng, JPG_QUALITY, currentSchedule, repeatByName);
                totalSuccess += res.success;
                totalFail += res.fail;
                if (res.details.length > 0) {
                    // Beri prefix nama dokumen pada detail artboard
                    for (var d = 0; d < res.details.length; d++) {
                        allDetails.push("[" + doc.name + "] " + res.details[d]);
                    }
                }
                
                // Tutup dokumen setelah selesai diexport
                doc.close(SaveOptions.DONOTSAVECHANGES);
                
                if (res.cancelled) {
                    allDetails.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                    break;
                }
            } catch(e) {
                totalFail++;
                allDetails.push(doc.name + " (Error: " + e.message + ")");
                if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                    allDetails.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                    break;
                }
            }
        }
        // Bersihkan clipboard setelah semua proses artboard selesai
        try {
            var clrDoc = app.documents.add(UnitValue(1, "px"), UnitValue(1, "px"), 72, "clip_clear", NewDocumentMode.RGB);
            clrDoc.selection.selectAll();
            clrDoc.selection.copy();
            clrDoc.close(SaveOptions.DONOTSAVECHANGES);
        } catch (e) {}
        try { app.activeDocument.selection.deselect(); } catch (e) {}

        var finalMsg = "Total Sukses: " + totalSuccess + ", Total Gagal: " + totalFail + "\n\n" + allDetails.join("\n");
        showScrollableAlert("Laporan Export Artboard (Batch)", finalMsg);
        return; // Stop di sini
    }

    var pasModeSub = 0;
    if (choice >= 300) {
        pasModeSub = choice - 300;
        choice = 3;
    }

    var INIT_MODE_JPG = (choice == 1 || choice == 5 || choice == 6);
    var INIT_MODE_KEEP_OPEN = (choice == 5);
    var INIT_MODE_ONLY_JPG = (choice == 6);
    var INIT_MODE_JPG_FOLDER = (false);
    var INIT_MODE_PNG_FOLDER = (false);
    var INIT_MODE_PNG = (choice == 2);
    var INIT_MODE_PAS = (choice == 3);
    var MODE_SMART = (choice == 99);

    var customTargetFolder = null;

    // 4. Proses Dokumen
    var docs = [];
    for (var i = 0; i < app.documents.length; i++) {
        docs.push(app.documents[i]);
    }

    var successList = [];
    var failList = [];
    var success2x3 = 0;
    var success4x6 = 0;

    for (var j = 0; j < docs.length; j++) {
        var doc = null;
        try {
            var MODE_JPG = INIT_MODE_JPG;
            var MODE_KEEP_OPEN = INIT_MODE_KEEP_OPEN;
            var MODE_ONLY_JPG = INIT_MODE_ONLY_JPG;
            var MODE_JPG_FOLDER = INIT_MODE_JPG_FOLDER;
            var MODE_PNG_FOLDER = INIT_MODE_PNG_FOLDER;
            var MODE_PNG = INIT_MODE_PNG;
            var MODE_PAS = INIT_MODE_PAS;

            doc = docs[j];
            app.activeDocument = doc;

            if (!doc.path && !customTargetFolder) {
                failList.push(doc.name + " (belum pernah disave & no custom folder)");
                continue;
            }

            var docPath;
            if (customTargetFolder) {
                try {
                    // Coba ambil path dokumen asal untuk mirroring folder
                    if (doc.path) {
                        var parentFolderName = doc.path.name;
                        var subFolder = new Folder(customTargetFolder + "/" + parentFolderName);
                        if (!subFolder.exists) subFolder.create();
                        docPath = subFolder.fsName;
                    } else {
                        docPath = customTargetFolder.fsName;
                    }
                } catch (e) {
                    // Fallback jika doc.path error (misal belum disave)
                    docPath = customTargetFolder.fsName;
                }
            } else {
                docPath = doc.path;
            }
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // --- SMART SAVE CHECK (Overrides Mode) ---
            if (MODE_SMART) {
                var has2x3 = new Folder(docPath + "/2x3").exists;
                var has4x6 = new Folder(docPath + "/4x6").exists;

                if (has2x3 || has4x6) {
                    MODE_PAS = true;
                    if (has2x3 && has4x6) {
                        pasModeSub = 4; // Kombi
                    } else if (has2x3) {
                        pasModeSub = 2; // 2x3
                    } else if (has4x6) {
                        pasModeSub = 3; // 4x6
                    }
                } else {
                    var jpgExists = new File(docPath + "/" + baseName + ".jpg").exists;
                    var pngExists = new File(docPath + "/" + baseName + ".png").exists;

                    if (jpgExists) {
                        MODE_JPG = true;
                    } else if (pngExists) {
                        MODE_PNG = true;
                    } else {
                        // Hanya Save PSD
                        doc.save();
                        doc.close(SaveOptions.DONOTSAVECHANGES);
                        successList.push(baseName + " (PSD Updated)");
                        continue; // Skip rest of loop
                    }
                }
            }

            // A. Save Master (PSD/PSB) - Skip if Only JPG/Folder mode
            if (!MODE_ONLY_JPG && !MODE_JPG_FOLDER && !MODE_PNG_FOLDER) {
                doc.save();
            }

            // B. Export Sesuai Mode
            if (MODE_JPG || MODE_PAS) {
                // Duplicate & Flatten
                var dupDoc = doc.duplicate(baseName + "_temp");
                app.activeDocument = dupDoc;

                // [FIX] Tambahkan layer baru (visible) sebelum flatten
                dupDoc.artLayers.add();
                dupDoc.flatten();

                // Action 'anti ramijud' (Skip if Only JPG/Folder mode)
                if (!MODE_ONLY_JPG && !MODE_JPG_FOLDER && !MODE_PNG_FOLDER) {
                    try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                }

                if (MODE_PAS) {
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                    successList.push(baseName + " (JPG Utama)");
                    var baseOutputFolder = docPath;
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    dupDoc = null;

                    if (pasModeSub == 2 || pasModeSub == 4) { // 2x3
                        var folder2x3 = createFolderIfNotExist(baseOutputFolder + "/2x3");
                        if (createCroppedVersion(doc, folder2x3, 2, 3, JPG_QUALITY)) success2x3++;
                    }
                    if (pasModeSub == 3 || pasModeSub == 4) { // 4x6
                        var folder4x6 = createFolderIfNotExist(baseOutputFolder + "/4x6");
                        if (createCroppedVersion(doc, folder4x6, 4, 6, JPG_QUALITY)) success4x6++;
                    }
                    if (pasModeSub != 1) successList.push(baseName + " (+Crops)");

                } else if (MODE_JPG) {
                    saveJPG(dupDoc, docPath + "/" + baseName + ".jpg", JPG_QUALITY);
                    successList.push(baseName + (MODE_JPG_FOLDER ? " (JPG Folder)" : " (PSD+JPG)"));
                    dupDoc.close(SaveOptions.DONOTSAVECHANGES);
                    
                    app.activeDocument = doc;
                    if (choice == 9) {
                        try {
                            var scriptFile = new File(new File($.fileName).parent + "/_kembalikan_seleksi.jsx");
                            if (scriptFile.exists) {
                                $.evalFile(scriptFile);
                            }
                        } catch (err) {}
                    }
                }

            } else if (MODE_PNG) {
                // Save PSD + PNG (Skip PSD if Folder mode)
                if (!MODE_PNG_FOLDER) {
                    var psdFile = new File(docPath + "/" + baseName + ".psd");
                    var psdOptions = new PhotoshopSaveOptions();
                    psdOptions.embedColorProfile = true;
                    psdOptions.layers = true;
                    psdOptions.maximizeCompatibility = true;
                    doc.saveAs(psdFile, psdOptions, true, Extension.LOWERCASE);
                }

                var dupDoc = doc.duplicate(baseName + "_forPNG");
                app.activeDocument = dupDoc;
                dupDoc.artLayers.add();
                executeAction(stringIDToTypeID("mergeVisible"), undefined, DialogModes.NO);

                if (!MODE_PNG_FOLDER) {
                    try { app.doAction("anti ramijud", "starter pack"); } catch (e) { }
                }

                savePNG(dupDoc, docPath + "/" + baseName + ".png");
                dupDoc.close(SaveOptions.DONOTSAVECHANGES);

                // Kembalikan Document Asli Menjadi Aktif
                app.activeDocument = doc;

                // SPECIAL LOGIC: Jika Mode PNG TETAP (choice == 10), jalankan _kembalikan_seleksi.jsx
                if (choice == 10) {
                    try {
                        var scriptFile = new File(new File($.fileName).parent + "/_kembalikan_seleksi.jsx");
                        if (scriptFile.exists) {
                            $.evalFile(scriptFile);
                        }
                    } catch (err) {
                        // Gagal eksekusi script, lewati saja
                    }
                }

                successList.push(baseName + (MODE_PNG_FOLDER ? " (PNG Folder)" : " (PSD+PNG)"));
            }

            // C. Close Original
            if (!MODE_KEEP_OPEN) {
                doc.close(SaveOptions.DONOTSAVECHANGES);
            }

        } catch (e) {
            failList.push((docs[j] ? docs[j].name : "Unknown") + " (Error: " + e.message + ")");
            // Jangan close PSD jika gagal, biarkan tetap terbuka
            
            // Deteksi jika user menekan ESC (User Cancelled)
            if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                failList.push("--- PROSES DIBATALKAN OLEH USER (ESC) ---");
                break; // Keluar dari loop dokumen
            }
        }
    }

    // 5. Laporan Final
    var msg = "Sukses: " + successList.length + "\n";
    if (successList.length > 0) msg += successList.join("\n") + "\n\n";
    msg += "Gagal: " + failList.length + "\n";
    if (failList.length > 0) msg += failList.join("\n");
    showScrollableAlert("Laporan Simpan Dokumen", msg);
}

// Global invocation
main();

// === HELPERS ===
function saveJPG(doc, filePath, quality) {
    var jpgOptions = new JPEGSaveOptions();
    jpgOptions.quality = quality;
    jpgOptions.embedColorProfile = true;
    jpgOptions.formatOptions = FormatOptions.STANDARDBASELINE;
    jpgOptions.scans = 3;
    doc.saveAs(new File(filePath), jpgOptions, true, Extension.LOWERCASE);
}

function savePNG(doc, filePath) {
    var pngOptions = new PNGSaveOptions();
    doc.saveAs(new File(filePath), pngOptions, true, Extension.LOWERCASE);
}

function createFolderIfNotExist(folderPath) {
    var folder = new Folder(folderPath);
    if (!folder.exists) folder.create();
    return folder;
}

function createCroppedVersion(sourceDoc, targetFolder, widthCm, heightCm, quality) {
    var baseName = sourceDoc.name.replace(/\.[^.]+$/, "");
    var tempDoc = sourceDoc.duplicate();
    app.activeDocument = tempDoc;

    var curW = tempDoc.width.as('px');
    var curH = tempDoc.height.as('px');
    var targetRatio = widthCm / heightCm;

    var cropW, cropH;
    if (curW / curH > targetRatio) {
        cropH = curH;
        cropW = Math.round(curH * targetRatio);
    } else {
        cropW = curW;
        cropH = Math.round(curW / targetRatio);
    }

    var left = Math.round((curW - cropW) / 2);
    var top = Math.round((curH - cropH) / 2);
    var right = left + cropW;
    var bottom = top + cropH;

    try {
        tempDoc.crop([UnitValue(left, 'px'), UnitValue(top, 'px'), UnitValue(right, 'px'), UnitValue(bottom, 'px')]);
    } catch (e) {
        try { tempDoc.crop([left, top, right, bottom]); } catch (e2) { }
    }

    tempDoc.resizeImage(UnitValue(widthCm, 'cm'), UnitValue(heightCm, 'cm'), tempDoc.resolution, ResampleMethod.BICUBIC);
    saveJPG(tempDoc, targetFolder + "/" + baseName + ".jpg", quality);
    tempDoc.close(SaveOptions.DONOTSAVECHANGES);
    return true;
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

function exportArtboards(sourceDoc, isPng, quality, schedule, repeatByName) {
    // Ensure the map exists even if this function is called before the top-level init.
    if (!processedUnchecked) processedUnchecked = {};
    // Revised export logic:
    // - Checked artboards (repeatByName[normalizedName] === true) are exported directly to the document's folder.
    // - Unchecked artboards are exported once per class folder into a sub‑folder named after the artboard.
    //   Subsequent exports for the same unchecked artboard will be skipped if the file already exists.
    var successCount = 0;
    var failCount = 0;
    var details = [];
    var basePath = sourceDoc.path.fsName;
    var baseName = sourceDoc.name.replace(/\.[^\.]+$/, "");
    var cancelled = false;

    var artboardsInfo = getArtboardsData();
    if (artboardsInfo.length === 0) {
        details.push("Tidak ada Artboard yang ditemukan.");
        return { success: 0, fail: 1, details: details, cancelled: cancelled };
    }

    var listToProcess = [];
    if (schedule !== undefined && schedule !== null) {
        for (var s = 0; s < schedule.length; s++) {
            listToProcess.push(artboardsInfo[schedule[s]]);
        }
    } else {
        listToProcess = artboardsInfo;
    }

    for (var i = 0; i < listToProcess.length; i++) {
        var ab = listToProcess[i];
        var abName = ab.name;
        try {
            var abBounds = ab.bounds;
            // 1. Create selection of the artboard area
            var region = [
                [abBounds[0], abBounds[1]], // left, top
                [abBounds[2], abBounds[1]], // right, top
                [abBounds[2], abBounds[3]], // right, bottom
                [abBounds[0], abBounds[3]]  // left, bottom
            ];
            app.activeDocument = sourceDoc;
            sourceDoc.selection.select(region);
            // 2. Copy merged pixels
            try {
                sourceDoc.selection.copy(true);
            } catch (e) {
                throw new Error("Area artboard kosong atau tidak bisa di-copy.");
            }
            sourceDoc.selection.deselect();
            // 3. Create a new document with the exact artboard size
            var w = abBounds[2] - abBounds[0];
            var h = abBounds[3] - abBounds[1];
            var newDoc = app.documents.add(UnitValue(w, "px"), UnitValue(h, "px"), sourceDoc.resolution, abName, NewDocumentMode.RGB);
            app.activeDocument = newDoc;
            // 4. Paste the copied pixels
            newDoc.paste();
            newDoc.flatten();
            try { app.doAction("anti ramijud", "starter pack"); } catch (e) {}
            var safeName = abName.replace(new RegExp('[\\\\/:*?"<>|]', 'g'), "_");
            // Determine if this artboard is marked as repeat (checked)
            var isRepeat = false;
            if (repeatByName) {
                var normAb = String(abName || "").replace(/^\s+|\s+$/g, "").toUpperCase();
                if (repeatByName.hasOwnProperty(normAb) && repeatByName[normAb]) {
                    isRepeat = true;
                }
            }
            var targetFolderPath;
            if (isRepeat) {
                // Checked artboards: export to the same folder as the PSD
                targetFolderPath = basePath;
            } else {
                // Unchecked artboards: export to a shared class folder (parent of the PSD folder)
                var classFolderPath = basePath;
                targetFolderPath = classFolderPath + "/" + safeName;
                var artboardFolder = new Folder(targetFolderPath);
                if (!artboardFolder.exists) artboardFolder.create();

                // Guard against duplicate export of unchecked artboards across PSDs in the same class
                var guardKey = classFolderPath + "|" + safeName;
                if (processedUnchecked.hasOwnProperty(guardKey)) {
                    details.push(abName + " (Skipped, already processed for this class)");
                    newDoc.close(SaveOptions.DONOTSAVECHANGES);
                    app.activeDocument = sourceDoc;
                    continue;
                }
                // Mark as processed
                processedUnchecked[guardKey] = true;
            }
            // File name: include PSD base name for checked artboards; for unchecked use just safeName
            var outName = baseName + "_" + safeName;

            var targetFile = new File(targetFolderPath + "/" + outName + (isPng ? ".png" : ".jpg"));
            // Skip export if the file already exists for non‑repeat artboards
            if (!isRepeat && targetFile.exists) {
                details.push(abName + " (Skipped, already exists)");
                newDoc.close(SaveOptions.DONOTSAVECHANGES);
                app.activeDocument = sourceDoc;
                continue;
            }
            // Save the file
            if (isPng) {
                savePNG(newDoc, targetFile.fsName);
            } else {
                saveJPG(newDoc, targetFile.fsName, quality);
            }
            newDoc.close(SaveOptions.DONOTSAVECHANGES);
            app.activeDocument = sourceDoc;
            successCount++;
            details.push(abName + " (Berhasil)");
        } catch (e) {
            failCount++;
            details.push(abName + " (Gagal: " + e.message + ")");
            if (app.activeDocument !== sourceDoc) {
                try { app.activeDocument.close(SaveOptions.DONOTSAVECHANGES); } catch (ex) {}
                app.activeDocument = sourceDoc;
            }
            if (e.number === 8007 || (e.message && e.message.toLowerCase().indexOf('cancel') !== -1)) {
                cancelled = true;
                break;
            }
        }
    }
    return { success: successCount, fail: failCount, details: details, cancelled: cancelled };
}

function selectLayerById(id) {
    var desc = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putIdentifier(charIDToTypeID("Lyr "), id);
    desc.putReference(charIDToTypeID("null"), ref);
    desc.putBoolean(charIDToTypeID("MkVs"), false);
    executeAction(charIDToTypeID("slct"), desc, DialogModes.NO);
}

function getArtboardsData() {
    var artboards = [];
    var doc = app.activeDocument;
    
    // Hanya iterasi layer di tingkat paling atas (root) untuk menghindari child layers
    for (var i = 0; i < doc.layers.length; i++) {
        var lyr = doc.layers[i];
        
        // Artboard di Photoshop DOM dibaca sebagai LayerSet (Group)
        if (lyr.typename === "LayerSet") {
            var isArtboard = false;
            var abBounds = [];
            try {
                var ref = new ActionReference();
                ref.putIdentifier(charIDToTypeID("Lyr "), lyr.id);
                var desc = executeActionGet(ref);
                
                // Cek secara ketat: artboardEnabled harus ada dan bernilai true
                if (desc.hasKey(stringIDToTypeID("artboardEnabled")) && desc.getBoolean(stringIDToTypeID("artboardEnabled"))) {
                    if (desc.hasKey(stringIDToTypeID("artboard"))) {
                        var abDesc = desc.getObjectValue(stringIDToTypeID("artboard"));
                        var rect = abDesc.getObjectValue(stringIDToTypeID("artboardRect"));
                        var top = rect.getDouble(stringIDToTypeID("top"));
                        var left = rect.getDouble(stringIDToTypeID("left"));
                        var bottom = rect.getDouble(stringIDToTypeID("bottom"));
                        var right = rect.getDouble(stringIDToTypeID("right"));
                        
                        abBounds = [left, top, right, bottom];
                        isArtboard = true;
                    } else {
                        var b = lyr.bounds;
                        abBounds = [b[0].as("px"), b[1].as("px"), b[2].as("px"), b[3].as("px")];
                        isArtboard = true;
                    }
                }
            } catch(e) {}
            
            if (isArtboard) {
                artboards.push({ name: lyr.name, id: lyr.id, bounds: abBounds });
            }
        }
    }
    return artboards;
}


function buildRegex(wordsStr) {
    var tmp = String(wordsStr||"").replace(/\r/g,"\n").split(/[,|\n]/);
    var words = [];
    for (var i=0;i<tmp.length;i++){
        var s = tmp[i].replace(/^\s+|\s+$/g,"");
        if (s.length>0) words.push(s);
    }
    return words.length === 0 ? null : words;
}

function fullLayerPath(layer) {
    var parts = [layer.name];
    var p = layer.parent;
    while (p && p.typename === "LayerSet") { parts.unshift(p.name); p = p.parent; }
    return parts.join(">");
}

function layerContainsBlacklist(text, wordsArray) {
    if (!text || !wordsArray) return false;
    var cleanText = text.replace(/^\s+|\s+$/g,"").toLowerCase();
    for (var i = 0; i < wordsArray.length; i++) {
        if (cleanText === wordsArray[i].toLowerCase()) return true; // EXACT MATCH 100%
    }
    return false;
}

function scanContainer(container, docName, wordsArray, results) {
    var layers = container.layers;
    for (var i=0;i<layers.length;i++){
        var L = layers[i];
        if (!L.visible) continue; // Abaikan layer atau grup yang disembunyikan (hide)
        
        if (L.typename === "ArtLayer" && L.kind === LayerKind.TEXT) {
            var isi = ""; try { isi = L.textItem.contents; } catch(e){}
            if (layerContainsBlacklist(isi, wordsArray)) {
                try { L.color = LayerColor.RED; } catch(e){}
                results.push(docName + " > " + fullLayerPath(L));
            }
        } else if (L.typename === "LayerSet") {
            scanContainer(L, docName, wordsArray, results);
        }
    }
}

function runTextCheckOnDocs(docs, wordsStr) {
    var wArray = buildRegex(wordsStr);
    if (!wArray) return { ok:false, hit:false, msg: "Daftar kalimat terlarang kosong." };

    var hasil = [];
    var originalDoc = app.activeDocument;
    for (var d=0; d<docs.length; d++){
        app.activeDocument = docs[d];
        scanContainer(docs[d], docs[d].name, wArray, hasil);
    }
    try { app.activeDocument = originalDoc; } catch(e){}

    if (hasil.length===0) return { ok:true, hit:false };
    return { ok:true, hit:true, msg:"Layer terdeteksi kalimat terlarang (Sama Persis):\n\n" + hasil.join("\n") + "\n\nTotal: " + hasil.length + " layer" };
}
