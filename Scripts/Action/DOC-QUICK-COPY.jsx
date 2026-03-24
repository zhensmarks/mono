#target photoshop

/*
    DOC-QUICK-COPY.jsx
    Otomatis menyalin data DOC (Nama, Alamat, Logo) ke clipboard secara berurutan.
    Setelah dijalankan, ketiga item akan masuk ke Windows Clipboard History (Win+V).
    Urutan: Nama → Alamat → Logo (gambar)
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

    var docName = "", docAddress = "", docLogo = "";
    try {
        var nameMatch = content.match(/"name":\s*"([^"]*)"/);
        var addressMatch = content.match(/"address":\s*"([^"]*)"/);
        var logoMatch = content.match(/"logo":\s*"([^"]*)"/);

        if (nameMatch) docName = nameMatch[1].replace(/\\n/g, "\r\n").replace(/\\\\/g, "\\");
        if (addressMatch) docAddress = addressMatch[1].replace(/\\n/g, "\r\n").replace(/\\\\/g, "\\");
        if (logoMatch) docLogo = logoMatch[1].replace(/\\\\/g, "\\");
    } catch (e) {
        alert("Gagal membaca data DOC: " + e);
        return;
    }

    // Helper: Jalankan PowerShell tersembunyi via VBScript agar tidak muncul CMD hitam
    function execHiddenPS(psCode) {
        var psFile = new File(Folder.temp + "/_bmachine_ps_temp.ps1");
        psFile.open("w");
        psFile.encoding = "UTF-8";
        psFile.write(psCode);
        psFile.close();

        var vbsFile = new File(Folder.temp + "/_bmachine_vbs_temp.vbs");
        vbsFile.open("w");
        vbsFile.write('Set WshShell = CreateObject("WScript.Shell")\n');
        vbsFile.write('WshShell.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""' + psFile.fsName + '""", 0, True\n');
        vbsFile.close();

        vbsFile.execute();
    }

    function copyText(text) {
        var txtEscaped = text.replace(/'/g, "''");
        execHiddenPS("Set-Clipboard -Value '" + txtEscaped + "'");
    }

    function copyImage(imgPath) {
        var imgEscaped = imgPath.replace(/'/g, "''");
        var psCode = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
                     "$img = [System.Drawing.Image]::FromFile('" + imgEscaped + "'); " +
                     "[System.Windows.Forms.Clipboard]::SetImage($img); " +
                     "$img.Dispose()";
        execHiddenPS(psCode);
    }

    // === EKSEKUSI OTOMATIS: Copy berurutan Nama → Alamat → Logo ===
    // Masing-masing masuk ke Clipboard History (Win+V)

    // 1. Copy Nama Sekolah
    if (docName) {
        copyText(docName);
        $.sleep(500); // Jeda agar clipboard history menangkap item
    }

    // 2. Copy Alamat
    if (docAddress) {
        copyText(docAddress);
        $.sleep(500);
    }

    // 3. Copy Logo (gambar)
    if (docLogo && new File(docLogo).exists) {
        copyImage(docLogo);
    }

})();
