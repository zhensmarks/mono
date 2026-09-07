#target photoshop

/*
    SEND-TEXT.jsx
    Mengganti teks nama & alamat di dokumen aktif Photoshop
*/

(function () {
    // Baca doc_info.json dari AppData\Roaming\BMachine.v2
    var appData = Folder.userData;
    var jsonFile = new File(appData + "/BMachine.v2/doc_info.json");

    if (!jsonFile.exists) {
        alert("File Data DOC belum ada.\nPastikan Anda telah mengisi tab DOC di BMachine dan menjalankan aplikasinya!");
        return;
    }

    jsonFile.open("r");
    var content = jsonFile.read();
    jsonFile.close();

    var docName = "", docAddress = "";
    try {
        var nameMatch = content.match(/"name":\s*"([^"]*)"/);
        var addressMatch = content.match(/"address":\s*"([^"]*)"/);
        
        if (nameMatch) docName = nameMatch[1].replace(/\\\\/g, "\\").replace(/\\n/g, "\r");
        if (addressMatch) docAddress = addressMatch[1].replace(/\\\\/g, "\\").replace(/\\n/g, "\r");
    } catch (e) {
        alert("Gagal membaca data DOC: " + e);
        return;
    }

    // Fungsi rekursif untuk mencari dan mengganti teks
    function replaceTextInLayers(layers, newName, newAddress) {
        for (var i = 0; i < layers.length; i++) {
            var layer = layers[i];
            if (layer.typename === "LayerSet") {
                replaceTextInLayers(layer.layers, newName, newAddress);
            } else if (layer.kind === LayerKind.TEXT) {
                var txt = layer.textItem.contents;
                var cleanTxt = txt.replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "").toUpperCase(); // Normalisasi spasi dan huruf besar
                
                if (newName !== "" && cleanTxt === "TK DELAPAN MATA AIR") {
                    layer.textItem.contents = newName;
                } else if (newAddress !== "" && (cleanTxt === "JL. SARI ENDAH NO. 7AGEGERKALONG HILIR BANDUNG" || cleanTxt === "JL. SARI ENDAH NO. 7A GEGERKALONG HILIR BANDUNG")) {
                    layer.textItem.contents = newAddress;
                }
            }
        }
    }

    if (app.documents.length > 0) {
        // Ganti teks nama & alamat jika dokumen aktif tersedia
        if (docName !== "" || docAddress !== "") {
            replaceTextInLayers(app.activeDocument.layers, docName, docAddress);
        }
    } else {
        alert("Tidak ada dokumen yang terbuka di Photoshop!");
    }

})();
