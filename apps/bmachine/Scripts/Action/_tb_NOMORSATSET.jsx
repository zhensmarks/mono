/*
    Batch OPEN DOCS:
    - Proses semua dokumen yang SEDANG TERBUKA (tidak buka file baru).
    - Ambil 2 digit paling depan dari nama dokumen (auto pad 07 jika 1 digit).
    - Isi ke layer teks bernama "HAL".
    - Opsional: auto-save setiap dokumen setelah update.

    Tidak ada dialog; langsung jalan ketika di-Play.
*/

#target photoshop
app.bringToFront();

(function () {
    // ===== KONFIGURASI =====
    var TARGET_LAYER_NAME = "HAL";   // ganti kalau perlu
    var AUTO_SAVE = false;            // true = simpan otomatis; false = jangan simpan

    if (!app.documents.length) {
        // Tidak ada alert supaya benar-benar “silent”; balik saja.
        return;
    }

    var ok = 0, skipNoDigits = 0, skipNoLayer = 0, skipNotText = 0, err = 0;

    // Iterasi semua dokumen yang terbuka
    // Pakai index dari 0..n-1 (kita tidak menutup dokumen, jadi aman)
    var docs = app.documents;
    for (var i = 0; i < docs.length; i++) {
        try {
            var doc = docs[i];
            app.activeDocument = doc;

            // Ambil nama file tanpa ekstensi (.psd/.psb/.jpg dll)
            var baseName = doc.name.replace(/\.[^\.]+$/, "");

            // Ambil digit paling depan
            var m = baseName.match(/^\s*(\d+)/);
            if (!m) {
                skipNoDigits++;
                continue;
            }

            // Ambil max 2 digit, pad jadi 2 kalau 1 digit
            var num = m[1].substr(0, 2);
            if (num.length === 1) num = "0" + num;

            // Cari layer target (rekursif)
            var target = findLayerByName(doc, TARGET_LAYER_NAME);
            if (!target) {
                skipNoLayer++;
                continue;
            }

            // Pastikan Text Layer
            if (!(target.typename === "ArtLayer" && target.kind === LayerKind.TEXT)) {
                skipNotText++;
                continue;
            }

            // Set isi teks
            target.textItem.contents = num;

            // Auto-save jika diminta
            if (AUTO_SAVE) {
                try { doc.save(); } catch (se) {}
            }

            ok++;
        } catch (e) {
            err++;
            // lanjut dokumen berikutnya
        }
    }

    // Tidak tampilkan alert agar benar-benar “langsung jalan”.
    // Kalau mau cek ringkasan, sementara bisa uncomment:
    /*
    alert(
        "Selesai.\n" +
        "OK: " + ok +
        "\nSkip (nama tidak diawali angka): " + skipNoDigits +
        "\nSkip (layer \"" + TARGET_LAYER_NAME + "\" tidak ada): " + skipNoLayer +
        "\nSkip (bukan Text Layer): " + skipNotText +
        "\nError: " + err
    );
    */

    // ===== Helper: cari layer by name (rekursif) =====
    function findLayerByName(parent, name) {
        var layers = parent.layers;
        for (var i = 0; i < layers.length; i++) {
            var L = layers[i];
            if (L.name === name) return L;
            if (L.typename === "LayerSet") {
                var found = findLayerByName(L, name);
                if (found) return found;
            }
        }
        return null;
    }
})();
