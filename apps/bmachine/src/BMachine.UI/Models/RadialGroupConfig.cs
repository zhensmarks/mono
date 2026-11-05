namespace BMachine.UI.Models;

/// <summary>
/// Satu grup custom di radial menu (selain grup bawaan TOOLBOX).
/// Disimpan di <c>__groups__.json</c> (folder AppData, bersebelahan dengan
/// scripts.json) sebagai <see cref="List{RadialGroupConfig}"/>.
/// </summary>
public class RadialGroupConfig
{
    /// <summary>Nama unik grup, dipakai sebagai key di __groups__.json (mis. "TB2", "FOTO").</summary>
    public string Key { get; set; } = "";

    /// <summary>Nama yang tampil di roda (mis. "KELAS B").</summary>
    public string Name { get; set; } = "";

    /// <summary>Kode pendek tampil di badan tombol (4 huruf max). Kosong = auto dari nama.</summary>
    public string Code { get; set; } = "";

    /// <summary>Key ikon resource (mis. "IconGrid", "IconGhost"). Kosong = IconGrid.</summary>
    public string IconKey { get; set; } = "";

    /// <summary>Urutan di antara item radial lain (semakin kecil makin dekat North).</summary>
    public int Order { get; set; }

    /// <summary>Nama file anggota (tanpa path, mis. "_tb_C-REDAKSI.jsx"). Path dibangun ulang
    /// dari folder Scripts/Action saat LoadScripts di RadialMenuViewModel.</summary>
    public List<string> Members { get; set; } = new();
}
