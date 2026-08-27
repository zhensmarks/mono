#target photoshop

/*
    DOC-QUICK-COPY.jsx (Modified: Send Logo to Photoshop)
    Langsung menempatkan logo ke dalam dokumen aktif di Photoshop, 
    atau membukanya sebagai dokumen baru jika tidak ada dokumen yang terbuka.
    Mendukung banyak logo (logo utama dan logo kedua).
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

    var docLogo = "", docLogo2 = "";
    try {
        var logoMatch = content.match(/"logo":\s*"([^"]*)"/);
        var logo2Match = content.match(/"logo2":\s*"([^"]*)"/);
        
        if (logoMatch) docLogo = logoMatch[1].replace(/\\\\/g, "\\");
        if (logo2Match) docLogo2 = logo2Match[1].replace(/\\\\/g, "\\");
    } catch (e) {
        alert("Gagal membaca data DOC: " + e);
        return;
    }

    var logosToProcess = [];
    if (docLogo && new File(docLogo).exists) logosToProcess.push(new File(docLogo));
    if (docLogo2 && new File(docLogo2).exists) logosToProcess.push(new File(docLogo2));

    if (logosToProcess.length > 0) {
        for (var i = 0; i < logosToProcess.length; i++) {
            var logoFile = logosToProcess[i];
            
            if (app.documents.length > 0) {
                // Tempatkan logo ke dalam dokumen yang sedang aktif
                var idPlc = charIDToTypeID("Plc ");
                var desc = new ActionDescriptor();
                desc.putPath(charIDToTypeID("null"), logoFile);
                desc.putEnumerated(charIDToTypeID("FTcs"), charIDToTypeID("QCSt"), charIDToTypeID("Qcsa"));
                executeAction(idPlc, desc, DialogModes.NO);
            } else {
                // Buka logo sebagai dokumen baru
                app.open(logoFile);
            }
        }
    } else {
        alert("Logo tidak ditemukan atau belum diatur di aplikasi BMachine!");
    }

})();
