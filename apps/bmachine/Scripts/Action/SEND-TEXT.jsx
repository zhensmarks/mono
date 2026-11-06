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
    // Terjemahkan escape JSON jadi karakter aslinya (\u0003, \n, \\, \").
    // Nilai dari doc_info.json bisa berisi escape seperti "TK DELAPAN\u0003MATA AIR"
    // (karakter kontrol ikut ter-copy dari WhatsApp/Trello); tanpa ini teksnya
    // tidak pernah cocok dengan layer PSD walau kelihatan sama di layar.
    function decodeEscapes(s) {
        if (!s) return "";
        return s
            .replace(/\\u([0-9a-fA-F]{4})/g, function (m, h) {
                return String.fromCharCode(parseInt(h, 16));
            })
            .replace(/\\r/g, "\r")
            .replace(/\\n/g, "\n")
            .replace(/\\t/g, "\t")
            .replace(/\\\\/g, "\\")
            .replace(/\\"/g, "\"");
    }

    function parseArray(json, key) {
        var re = new RegExp("\"" + key + "\"\\s*:\\s*\\[([^\\]]*)\\]");
        var m = json.match(re);
        if (!m) return null;
        var items = [];
        var inner = m[1];
        var reItem = /"((?:\\.|[^"\\])*)"/g;
        var im;
        while ((im = reItem.exec(inner)) !== null) {
            items.push(decodeEscapes(im[1]));
        }
        return items.length > 0 ? items : null;
    }

    try {
        var nameMatch = content.match(/"name":\s*"([^"]*)"/);
        var addressMatch = content.match(/"address":\s*"([^"]*)"/);

        if (nameMatch) docName = decodeEscapes(nameMatch[1]);
        if (addressMatch) docAddress = decodeEscapes(addressMatch[1]).replace(/\r\n|\n|\r/g, "\r");

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
    // Normalisasi: buang SEMUA karakter selain huruf/angka, lalu huruf besar.
    // Spasi, tab, ganti baris, tanda baca (titik/koma), DAN karakter kontrol
    // (mis. 0x03 yang ikut ter-copy dari WhatsApp) semuanya dibuang. Inilah yang
    // membuat teks sumber "JL. SARI ENDAH NO. 7A" tetap cocok dengan layer
    // "JL SARI ENDAH NO 7A" maupun dengan teks yang mengandung karakter aneh.
    function normalize(s) {
        return (s || "")
            .replace(/[^0-9A-Za-z]/g, "")
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

    // ------------------------------------------------------------------
    // Deteksi berdasarkan NAMA LAYER (permintaan user).
    // Sebagian PSD tidak memakai teks placeholder yang persis sama dengan
    // daftar di Pengaturan DOC (beda ejaan / beda tanda baca), tapi nama
    // layer-nya konsisten: "NAMA SEKOLAH", "ALAMAT", "NAMA & ALAMAT".
    // Layer seperti itu tetap diisi walau teksnya tidak cocok.
    // ------------------------------------------------------------------
    var RE_LAYER_ADDR = /ALAMAT|ALMT|ADDRESS|ADDR/;
    var RE_LAYER_NAME = /NAMA|SCHOOLNAME|SEKOLAH/;
    // Kata yang menandakan layer itu BUKAN untuk nama/alamat sekolah.
    var RE_LAYER_NEG = /GURU|SISWA|MURID|ANAK|KELAS|TAHUN|ANGKATAN|TANGGAL|NPSN|NSS|TELP|TELEPON|HANDPHONE|EMAIL|WEBSITE|WALI|KEPALA|ORTU|ORANGTUA|LOGO|FOTO|JABATAN|NIP|NIK/;

    // Kembalikan "name" | "address" | "both" | "" untuk sebuah nama layer.
    function classifyLayer(layerName) {
        var n = (layerName || "").replace(/[^0-9A-Za-z]/g, "").toUpperCase();
        if (n === "") return "";
        if (RE_LAYER_NEG.test(n)) return "";
        var isAddr = RE_LAYER_ADDR.test(n);
        var isName = RE_LAYER_NAME.test(n);
        if (isAddr && /NAMA|SCHOOLNAME/.test(n)) return "both";
        if (isAddr) return "address";
        if (isName) return "name";
        return "";
    }

    var replacedCount = 0;
    var byLayerCount = 0;  // berapa yang diisi karena NAMA LAYER, bukan teks
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
                var applied = "";

                // 1. Prioritas tertinggi: teksnya PERSIS salah satu teks sumber
                //    yang diisi di Pengaturan DOC.
                if (newName !== "" && nameSet[cleanTxt] === true) {
                    layer.textItem.contents = newName;
                    applied = "name";
                } else if (newAddress !== "" && addressSet[cleanTxt] === true) {
                    layer.textItem.contents = newAddress;
                    applied = "address";
                } else {
                    // 2. Cadangan: tebakan dari NAMA LAYER dokumen.
                    var kind = classifyLayer(layer.name);
                    if (kind === "both" && newName !== "" && newAddress !== "") {
                        layer.textItem.contents = newName + "\r" + newAddress;
                        applied = "both";
                    } else if ((kind === "name" || kind === "both") && newName !== "") {
                        layer.textItem.contents = newName;
                        applied = "name";
                    } else if ((kind === "address" || kind === "both") && newAddress !== "") {
                        layer.textItem.contents = newAddress;
                        applied = "address";
                    }
                    if (applied !== "") byLayerCount++;
                }

                if (applied !== "") replacedCount++;
                else rememberFound(raw);
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
        msg += "Nama layer yang dikenali untuk nama/alamat sekolah: NAMA SEKOLAH,\n";
        msg += "ALAMAT, atau satu layer gabungan 'NAMA & ALAMAT'.\n\n";
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
    } else if (byLayerCount > 0) {
        // Sebagian/semua penggantian memakai nama layer (bukan teks sumber),
        // beri tahu supaya user tahu apa yang tadi tidak terdeteksi.
        alert("Teks diganti: " + replacedCount + " layer.\n" +
              byLayerCount + " di antaranya dikenali dari NAMA LAYER " +
              "(teksnya tidak ada di daftar Pengaturan DOC).\n\n" +
              "Kalau ada yang salah ganti, tambahkan teks asli layer itu ke " +
              "Pengaturan DOC > teks sumber.");
    }
})();
