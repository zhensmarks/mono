#target photoshop

/*
    SEND-TEXT.jsx
    Mengganti teks nama & alamat di dokumen aktif Photoshop.

    Teks asal (placeholder) yang dicari TIDAK lagi di-hardcode di sini.
    Daftar teks sumber dibaca dari doc_info.json (key: nameSources &
    addressSources) yang diatur lewat tab DOC > Pengaturan (ikon gerigi)
    di BMachine. Tidak perlu restart aplikasi: file JSON ditulis ulang
    setiap kali Anda menekan Simpan.
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

    // Daftar teks sumber (bawaan, dipakai bila JSON tidak menyediakannya).
    var nameSources = ["TK DELAPAN MATA AIR"];
    var addressSources = [
        "JL. SARI ENDAH NO. 7AGEGERKALONG HILIR BANDUNG",
        "JL. SARI ENDAH NO. 7A GEGERKALONG HILIR BANDUNG"
    ];

    // Ambil array string dari JSON memakai regex sederhana (aman untuk Photoshop
    // tanpa JSON bawaan). Contoh cocok: "nameSources": ["A", "B"].
    function parseArray(json, key) {
        var re = new RegExp("\"" + key + "\"\\s*:\\s*\\[([^\\]]*)\\]");
        var m = json.match(re);
        if (!m) return null;
        var items = [];
        var inner = m[1];
        var reItem = /"((?:\\.|[^"\\])*)"/g;
        var im;
        while ((im = reItem.exec(inner)) !== null) {
            items.push(im[1].replace(/\\\\/g, "\\").replace(/\\"/g, "\""));
        }
        return items.length > 0 ? items : null;
    }

    try {
        var nameMatch = content.match(/"name":\s*"([^"]*)"/);
        var addressMatch = content.match(/"address":\s*"([^"]*)"/);

        if (nameMatch) docName = nameMatch[1].replace(/\\\\/g, "\\").replace(/\\n/g, "\r");
        if (addressMatch) docAddress = addressMatch[1].replace(/\\\\/g, "\\").replace(/\\n/g, "\r");

        var cfgNameSources = parseArray(content, "nameSources");
        if (cfgNameSources) nameSources = cfgNameSources;

        var cfgAddressSources = parseArray(content, "addressSources");
        if (cfgAddressSources) addressSources = cfgAddressSources;
    } catch (e) {
        alert("Gagal membaca data DOC: " + e);
        return;
    }

    // Normalisasi agresif: buang SEMUA jenis spasi (spasi, tab, ganti baris)
    // lalu jadikan huruf besar. Dengan begitu perbedaan posisi ganti-baris
    // atau spasi berlebih di layer PSD tidak menyebabkan gagal cocok.
    function normalize(s) {
        return (s || "")
            .replace(/\s+/g, "")
            .toUpperCase();
    }

    // Bangun set sumber yang sudah dinormalisasi untuk pencocokan cepat.
    function buildSet(list) {
        var set = {};
        for (var i = 0; i < list.length; i++) {
            var n = normalize(list[i]);
            if (n !== "") set[n] = true;
        }
        return set;
    }

    var nameSet = buildSet(nameSources);
    var addressSet = buildSet(addressSources);

    var replacedCount = 0;
    var foundTexts = [];   // untuk diagnosa bila tidak ada yang cocok

    function rememberFound(txt) {
        var t = (txt || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
        if (t === "") return;
        for (var k = 0; k < foundTexts.length; k++) {
            if (foundTexts[k] === t) return;
        }
        if (foundTexts.length < 25) foundTexts.push(t);
    }

    // Fungsi rekursif untuk mencari dan mengganti teks.
    function replaceTextInLayers(layers, newName, newAddress) {
        for (var i = 0; i < layers.length; i++) {
            var layer = layers[i];
            if (layer.typename === "LayerSet") {
                replaceTextInLayers(layer.layers, newName, newAddress);
            } else if (layer.kind === LayerKind.TEXT) {
                var raw = layer.textItem.contents;
                var cleanTxt = normalize(raw);

                if (newName !== "" && nameSet[cleanTxt] === true) {
                    layer.textItem.contents = newName;
                    replacedCount++;
                } else if (newAddress !== "" && addressSet[cleanTxt] === true) {
                    layer.textItem.contents = newAddress;
                    replacedCount++;
                } else {
                    rememberFound(raw);
                }
            }
        }
    }

    if (app.documents.length === 0) {
        alert("Tidak ada dokumen yang terbuka di Photoshop!");
        return;
    }

    if (docName === "" && docAddress === "") {
        alert("Nama & Alamat di tab DOC masih kosong. Isi dulu di BMachine.");
        return;
    }

    replaceTextInLayers(app.activeDocument.layers, docName, docAddress);

    if (replacedCount === 0) {
        // Tidak ada yang cocok -> tampilkan teks yang ADA di PSD supaya bisa
        // disalin persis ke Pengaturan DOC.
        var msg = "Tidak ada teks yang cocok untuk diganti.\n\n";
        msg += "Teks sumber yang dicari (dari Pengaturan DOC):\n";
        msg += "  Nama    : " + nameSources.join(" | ") + "\n";
        msg += "  Alamat  : " + addressSources.join(" | ") + "\n\n";
        if (foundTexts.length > 0) {
            msg += "Teks yang ditemukan di layer PSD:\n";
            for (var j = 0; j < foundTexts.length; j++) {
                msg += "  \u2022 " + foundTexts[j] + "\n";
            }
            msg += "\nSalin teks di atas (persis) ke Pengaturan DOC > teks sumber, lalu Simpan.";
        } else {
            msg += "Tidak ada layer teks (type layer) yang ditemukan di dokumen.";
        }
        alert(msg);
    }
})();
