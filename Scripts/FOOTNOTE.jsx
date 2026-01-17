#target photoshop

// Cek dokumen aktif
if (app.documents.length === 0) {
    alert("Tidak ada dokumen yang terbuka!");
} else {

    // === BUAT UI ===
    var w = new Window("dialog", "Buat Footnote");
    w.orientation = "column";
    w.alignChildren = ["fill", "center"];
    w.spacing = 10;
    w.margins = 18;

    // Set background window (biru gelap, sama seperti save-format.jsx)
    var bgColor = [0.18, 0.22, 0.28, 1]; // RGBA 0-1
    w.graphics.backgroundColor = w.graphics.newBrush(w.graphics.BrushType.SOLID_COLOR, bgColor);

    // Judul
    var judul = w.add("statictext", undefined, "BUAT FOOTNOTE");
    judul.alignment = "center";
    judul.graphics.font = ScriptUI.newFont(judul.graphics.font.name, "bold", 16);
    judul.graphics.foregroundColor = judul.graphics.newPen(judul.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);

    // Panel garis
    var garis = w.add("panel", undefined, undefined, {borderStyle:"sunken"});
    garis.preferredSize = [350, 2];

    // Pilih Jenis
    var jenisGroup = w.add("group");
    jenisGroup.orientation = "column";
    jenisGroup.alignChildren = ["fill", "center"];
    jenisGroup.graphics.backgroundColor = jenisGroup.graphics.newBrush(jenisGroup.graphics.BrushType.SOLID_COLOR, bgColor);
    var lblJenis = jenisGroup.add("statictext", undefined, "Pilih Jenis:");
    lblJenis.alignment = "center";
    lblJenis.graphics.foregroundColor = lblJenis.graphics.newPen(lblJenis.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);
    var tambList = jenisGroup.add("dropdownlist", undefined, ["NON REG", "FS", "BERSAMA", "FREE POSTER", "FREE KALENDER"]);
    tambList.selection = 0;
    tambList.preferredSize.width = 220;

    // Pilih Ukuran
    var ukuranGroup = w.add("group");
    ukuranGroup.orientation = "column";
    ukuranGroup.alignChildren = ["fill", "center"];
    ukuranGroup.graphics.backgroundColor = ukuranGroup.graphics.newBrush(ukuranGroup.graphics.BrushType.SOLID_COLOR, bgColor);
    var lblUkuran = ukuranGroup.add("statictext", undefined, "Pilih Ukuran:");
    lblUkuran.alignment = "center";
    lblUkuran.graphics.foregroundColor = lblUkuran.graphics.newPen(lblUkuran.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);
    var sizeList = ukuranGroup.add("dropdownlist", undefined, ["8R", "10RP", "12RP", "16RP", "20RP", "24RP"]);
    sizeList.selection = 1;
    sizeList.preferredSize.width = 220;

    // Nama sekolah
    var sekolahGroup = w.add("group");
    sekolahGroup.orientation = "column";
    sekolahGroup.alignChildren = ["fill", "center"];
    sekolahGroup.graphics.backgroundColor = sekolahGroup.graphics.newBrush(sekolahGroup.graphics.BrushType.SOLID_COLOR, bgColor);
    var lblSekolah = sekolahGroup.add("statictext", undefined, "Nama Sekolah:");
    lblSekolah.alignment = "center";
    lblSekolah.graphics.foregroundColor = lblSekolah.graphics.newPen(lblSekolah.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);
    var sekolahField = sekolahGroup.add("edittext", undefined, "");
    sekolahField.preferredSize.width = 220;

    // Pilihan warna
    var warnaGroup = w.add("group");
    warnaGroup.orientation = "column";
    warnaGroup.alignChildren = ["fill", "center"];
    warnaGroup.graphics.backgroundColor = warnaGroup.graphics.newBrush(warnaGroup.graphics.BrushType.SOLID_COLOR, bgColor);
    var lblWarna = warnaGroup.add("statictext", undefined, "Warna Teks:");
    lblWarna.alignment = "center";
    lblWarna.graphics.foregroundColor = lblWarna.graphics.newPen(lblWarna.graphics.PenType.SOLID_COLOR, [1,1,1,1], 1);
    // TAMBAHKAN HIJAU
    var warnaList = warnaGroup.add("dropdownlist", undefined, ["Merah", "Putih", "Hitam", "Hijau"]);
    warnaList.selection = 0;
    warnaList.preferredSize.width = 220;

    // Tombol
    var btnGroup = w.add("group");
    btnGroup.alignment = "center";
    btnGroup.graphics.backgroundColor = btnGroup.graphics.newBrush(btnGroup.graphics.BrushType.SOLID_COLOR, bgColor);
    var okBtn = btnGroup.add("button", undefined, "Buat");
    var allBtn = btnGroup.add("button", undefined, "Terapkan ke Semua Dokumen"); // Tambahkan tombol ini
    var cancelBtn = btnGroup.add("button", undefined, "Batal");

    // Fungsi membuat footnote pada dokumen aktif
    function buatFootnote(doc, footnoteText, warnaPilihan) {
        var textLayer = doc.artLayers.add();
        textLayer.kind = LayerKind.TEXT;
        var textItem = textLayer.textItem;
        textItem.contents = footnoteText;

        try {
            textItem.font = "Geomatrix-Black";
        } catch (e) {
            try {
                textItem.font = "Arial-Black";
            } catch (e2) {
                textItem.font = "ArialMT";
            }
        }

        textItem.size = 6;

        var c = new SolidColor();
        if (warnaPilihan === "Merah") {
            c.rgb.red = 255; c.rgb.green = 0; c.rgb.blue = 0;
        } else if (warnaPilihan === "Putih") {
            c.rgb.red = 255; c.rgb.green = 255; c.rgb.blue = 255;
        } else if (warnaPilihan === "Hijau") {
            c.rgb.red = 0; c.rgb.green = 255; c.rgb.blue = 0;
        } else {
            c.rgb.red = 0; c.rgb.green = 0; c.rgb.blue = 0;
        }
        textItem.color = c;

        textItem.position = [1.5, doc.height - 0.7];
    }

    // Event tombol "Buat" (satu dokumen)
    okBtn.onClick = function() {
        var tamb = tambList.selection.text.toUpperCase();
        var ukuran = sizeList.selection.text.toUpperCase();
        var sekolah = sekolahField.text.toUpperCase();
        var warnaPilihan = warnaList.selection.text;

        if (sekolah === "") {
            alert("Nama sekolah tidak boleh kosong!");
            return;
        }

        var footnoteText = tamb + "_" + ukuran + "_" + sekolah;
        var doc = app.activeDocument;
        buatFootnote(doc, footnoteText, warnaPilihan);

        w.close();
    };

    // Event tombol "Terapkan ke Semua Dokumen"
    allBtn.onClick = function() {
        var tamb = tambList.selection.text.toUpperCase();
        var ukuran = sizeList.selection.text.toUpperCase();
        var sekolah = sekolahField.text.toUpperCase();
        var warnaPilihan = warnaList.selection.text;

        if (sekolah === "") {
            alert("Nama sekolah tidak boleh kosong!");
            return;
        }

        var footnoteText = tamb + "_" + ukuran + "_" + sekolah;

        for (var i = 0; i < app.documents.length; i++) {
            app.activeDocument = app.documents[i];
            buatFootnote(app.activeDocument, footnoteText, warnaPilihan);
        }

        w.close();
    };

    cancelBtn.onClick = function() {
        w.close();
    };

    w.center();
    w.show();
}
