using CommunityToolkit.Mvvm.Messaging.Messages;
using BMachine.UI.Models;

namespace BMachine.UI.Messages;

/// <summary>
/// Hasil cek update (satu kali, saat startup) yang dibagikan ke VM lain — badge
/// "ada pembaruan" di popup akun. VM lain hanya menyimpan hasilnya di memori,
/// jadi membuka popup tidak pernah membaca ulang folder share (tanpa lag).
/// </summary>
public class UpdateStateMessage : ValueChangedMessage<UpdateInfo>
{
    public UpdateStateMessage(UpdateInfo value) : base(value)
    {
    }
}
