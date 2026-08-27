#target photoshop

(function () {
    if (app.documents.length === 0) return;

    function findLayerRecursive(root, name) {
        try { return root.layers.getByName(name); } catch (e) { }
        for (var i = 0; i < root.layers.length; i++) {
            if (root.layers[i].typename == "LayerSet") {
                var found = findLayerRecursive(root.layers[i], name);
                if (found) return found;
            }
        }
        return null;
    }

    function showLayers(doc, names) {
        for (var i = 0; i < names.length; i++) {
            var layer = findLayerRecursive(doc, names[i]);
            if (layer) layer.visible = true;
        }
    }

    // Mengembalikan semua layer yang mungkin disembunyikan oleh seleksi_alam.jsx
    var listAll = [
        // List baru
        "REDAKSI SEKOLAH", "ASET", "KOLASE", "BG KOLASE", "BG", 
        "PROP", "KARPET", "karpet", "UTAMA",
        
        // List lama (dibiyarkan untuk kompatibilitas jika ada file lama)
        "KECIL", "BG KECIL", "BESAR", "BG ORTU", "KECIL ORTU"
    ];

    // Ambil nama file untuk layer dinamis (misal "3" dari "3.psd")
    var baseName = app.activeDocument.name.replace(/\.[^\.]+$/, "");
    var dynamicLayers = [
        baseName + "(1)", baseName + " (1)",
        baseName + "(2)", baseName + " (2)",
        baseName + "(3)", baseName + " (3)"
    ];
    
    // Gabungkan list dasar dengan list dinamis
    listAll = listAll.concat(dynamicLayers);

    showLayers(app.activeDocument, listAll);
})();
