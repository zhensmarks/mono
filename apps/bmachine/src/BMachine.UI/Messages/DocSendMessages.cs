using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BMachine.UI.Messages;

/// <summary>Satu entri tab DOC (hanya id, nomor, judul) untuk submenu radial.</summary>
public sealed class DocTabSnapshot
{
    public DocTabSnapshot(string id, int number, string title)
    {
        Id = id;
        Number = number;
        Title = title;
    }

    public string Id { get; }
    public int Number { get; }
    public string Title { get; }
}

/// <summary>
/// Radial menu meminta daftar tab DOC yang sedang ada.
/// BatchViewModel membalas dengan snapshot ringan; bila tidak ada yang
/// membalas (BatchViewModel belum dibuat), radial menu tetap memakai
/// perilaku lama (kirim langsung pakai doc_info.json tab aktif).
/// </summary>
public class GetDocTabsRequestMessage : RequestMessage<IReadOnlyList<DocTabSnapshot>> { }

/// <summary>
/// Radial menu meminta BatchViewModel menulis doc_info.json dari tab DOC
/// tertentu SEBELUM SEND-TEXT.jsx / SEND-LOGO.jsx dijalankan.
/// Balasan true = doc_info.json sudah mencerminkan tab tsb.
/// </summary>
public class PrepareDocSendRequestMessage : RequestMessage<bool>
{
    public PrepareDocSendRequestMessage(string tabId) => TabId = tabId;
    public string TabId { get; }
}