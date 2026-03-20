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
            "REDAKSI SEKOLAH", "ASET", "KECIL", "BG KECIL",
            "PROP", "KARPET", "karpet", "BG", "BESAR",
            "BG ORTU", "KECIL ORTU"
        ];

        showLayers(app.activeDocument, listAll);
    })();
