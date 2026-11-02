#target photoshop

/*
    TOOLBOX.jsx
    Dispatcher JSX: memindai folder Scripts/Action, menampilkan daftar .jsx
    ber-mark "_tb_" sebagai tombol, lalu mengeksekusi yang diklik via $.evalFile.

    ATURAN PENULISAN SCRIPT "_tb_*.jsx" (disiplin posisi & tampilan):
      • Wajib punya persistensi posisi window: baca/tulis
        Folder.userData + "/bmachine_<nama_script>_pos.json"
      • Format JSON: {"x": N, "y": N}  (N = integer, posisi window)
      • Baca posisi SEBELUM window.show(); tulis SETELAH user tutup (res != 0)
      • Template utils (copy-paste ke tiap script):
          function loadWindowLocation() {
              var f = new File(Folder.userData + "/bmachine_<NAMA>_pos.json");
              if (f.exists) { f.open("r"); var d = f.read(); f.close();
                  try { var o = eval("(" + d + ")");
                        if (o && typeof o.x === "number" && typeof o.y === "number") return o; } catch(e){}
              }
              return null;
          }
          function saveWindowLocation(loc) {
              if (!loc) return;
              var f = new File(Folder.userData + "/bmachine_<NAMA>_pos.json");
              f.open("w");
              f.write('{"x": ' + Math.round(loc.x) + ', "y": ' + Math.round(loc.y) + '}');
              f.close();
          }
          // pakai: var loc = loadWindowLocation();
          //       if (loc) w.location = [loc.x, loc.y]; else w.center();
          //       var res = w.show(); if (res != 0) saveWindowLocation(w.location);
      • Tampilan dialog: w.spacing = 10; w.margins = 16 (standar BMachine)

    KONVENSI PENAMAAN FOLDER SCRIPTS/ACTION (tanpa edit kode toolbox):
      • awalan "_tb_"       → TAMPIL DI TOOLBOX SAJA (di-skip radial menu)
      • tanpa awalan        → tampil di radial menu biasa, TIDAK di toolbox
      • awalan "_" lainnya → tersembunyi dari keduanya (internal helper)

    Cara pakai:
    - Mau script masuk toolbox? rename: seleksi_alam.jsx → _tb_seleksi_alam.jsx
    - File biasa (tanpa "_tb_") tidak akan pernah muncul di toolbox.
    - Klik tombol Refresh setelah rename/tambah file.
*/

(function () {
    var selfFile = new File($.fileName);
    var actionDir = selfFile.parent; // Folder: Scripts/Action

    // Persistensi posisi toolbox sendiri
    var TOOLBOX_POS = new File(Folder.userData + "/bmachine_toolbox_pos.json");
    function loadToolboxPos() {
        if (!TOOLBOX_POS.exists) return null;
        TOOLBOX_POS.open("r");
        var d = TOOLBOX_POS.read();
        TOOLBOX_POS.close();
        try {
            var o = eval("(" + d + ")");
            if (o && typeof o.x === "number" && typeof o.y === "number") return o;
        } catch (e) { }
        return null;
    }
    function saveToolboxPos(loc) {
        if (!loc) return;
        TOOLBOX_POS.open("w");
        TOOLBOX_POS.write('{"x": ' + Math.round(loc.x) + ', "y": ' + Math.round(loc.y) + '}');
        TOOLBOX_POS.close();
    }

    // Hanya file awalan "_tb_" yang masuk toolbox
    var list = [];
    if (actionDir instanceof Folder) {
        var allFiles = actionDir.getFiles();
        for (var i = 0; i < allFiles.length; i++) {
            var f = allFiles[i];
            if (!(f instanceof File) || !(f.parent instanceof Folder) || f.parent.fsName !== actionDir.fsName) continue;
            var fn = f.name;
            if (!fn.match(/\.jsx$/i)) continue;
            // Hanya file awalan "_tb_" yang masuk toolbox
            if (!fn.match(/^_tb_/i)) continue;
            // Display name: buang prefix "_tb_" dan ".jsx"
            var displayName = fn.replace(/^_tb_/i, "").replace(/\.jsx$/i, "");
            list.push({ name: displayName, file: f });
        }
    }

    // Sort alfabet
    list.sort(function (a, b) {
        return a.name.toLowerCase() < b.name.toLowerCase() ? -1 :
               a.name.toLowerCase() > b.name.toLowerCase() ? 1 : 0;
    });

    // === UI ===
    var w = new Window("dialog", "TOOLBOX — Action");
    w.orientation = "column";
    w.alignChildren = ["fill", "top"];
    w.spacing = 10;
    w.margins = 16;

    // Posisi awal: ingat posisi terakhir / di tengah
    var savedLoc = loadToolboxPos();
    if (savedLoc) w.location = [savedLoc.x, savedLoc.y];
    else w.center();

    if (list.length === 0) {
        w.add("statictext", undefined, "Toolbox kosong.\nTidak ada .jsx ber-mark _tb_ di folder: " + actionDir.fsName);
        var grpEmpty = w.add("group"); grpEmpty.alignment = "center";
        grpEmpty.add("button", undefined, "Tutup");
        var res0 = w.show();
        if (res0 != 0) saveToolboxPos(w.location);
        return;
    }

    // Grid tombol: < 4 item = 1 kolom, >= 4 item = 2 kolom
    var cols = list.length >= 4 ? 2 : 1;
    var half = Math.ceil(list.length / cols); // item per kolom (kiri lebih penuh)
    var container = w.add("group");
    container.orientation = "row";
    container.alignChildren = ["fill", "top"];
    container.spacing = 8;

    for (var c = 0; c < cols; c++) {
        var colPanel = container.add("group");
        colPanel.orientation = "column";
        colPanel.alignChildren = ["fill", "top"];
        colPanel.spacing = 6;
        for (var idx = c * half; idx < Math.min((c + 1) * half, list.length); idx++) {
            (function (i) {
                var btn = colPanel.add("button", undefined, "  ▶  " + list[i].name);
                btn.alignment = "fill";
                btn.onClick = function () {
                    var chosen = list[i].file;
                    saveToolboxPos(w.location);
                    w.close(1);
                    try { $.evalFile(chosen); } catch (e) { alert("Gagal menjalankan " + chosen.name + ":\n" + e); }
                };
            })(idx);
        }
    }

    // Tombol bawah
    var grpBottom = w.add("group");
    grpBottom.alignment = "center";
    var btnRefresh = grpBottom.add("button", undefined, "↻ Refresh");
    var btnBatal = grpBottom.add("button", undefined, "Batal", { name: "cancel" });
    btnRefresh.onClick = function () { saveToolboxPos(w.location); w.close(0); $.evalFile(selfFile); };
    btnBatal.onClick = function () { saveToolboxPos(w.location); w.close(0); };

    var res = w.show();
    if (res != 0) saveToolboxPos(w.location);
})();
