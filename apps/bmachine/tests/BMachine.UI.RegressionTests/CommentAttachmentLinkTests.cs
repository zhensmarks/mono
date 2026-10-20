using System;
using System.Collections.Generic;
using BMachine.UI.Models;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Foto yang diunggah sebagai attachment harus muncul DI DALAM bubble comment.
/// Loader menyisipkan markdown "![attachment](url)" ke teks comment, lalu
/// FormattedTextBlock merendernya jadi gambar inline (klik = lightbox).
/// Pasangan dipilih: foto maksimal 5 menit sebelum comment (atau 60 detik sesudahnya)
/// dan uploader = penulis comment.
/// </summary>
public class CommentAttachmentLinkTests
{
    private static readonly DateTime T0 = new(2026, 10, 20, 10, 0, 0);

    private static TrelloComment Comment(string id, string text, DateTime date, string memberId, bool isActivity = false)
        => new() { Id = id, Text = text, Date = date, MemberCreatorId = memberId, IsActivity = isActivity };

    private static List<(DateTime Date, string MemberId, string Url)> Attachments(params (DateTime, string, string)[] items)
    {
        var list = new List<(DateTime, string, string)>();
        foreach (var i in items) list.Add(i);
        return list;
    }

    [Fact]
    public void Foto_2_menit_sebelum_comment_ditempel_ke_comment_itu()
    {
        var comments = new List<TrelloComment> { Comment("c1", "cek hasil", T0, "M1") };
        var attachments = Attachments((T0.AddSeconds(-120), "M1",
            "https://trello.com/1/cards/abc/attachments/xyz/preview.png"));

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        Assert.Contains("![attachment](https://trello.com/1/cards/abc/attachments/xyz/preview.png)", comments[0].Text);
        Assert.StartsWith("cek hasil", comments[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Foto_terlalu_jauh_atau_uploader_bedah_tidak_ditempel()
    {
        var comments = new List<TrelloComment> { Comment("c1", "tes", T0, "M1") };
        var attachments = Attachments(
            (T0.AddMinutes(-10), "M1", "https://trello.com/1/cards/a/attachments/jauh.png"),   // 10 menit sebelum
            (T0.AddSeconds(-30), "M2", "https://trello.com/1/cards/a/attachments/oranglain.png")); // uploader beda

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        Assert.Equal("tes", comments[0].Text);
        Assert.DoesNotContain("![attachment]", comments[0].Text);
    }

    [Fact]
    public void Row_activity_tidak_pernah_mendapat_sisipan_gambar()
    {
        var comments = new List<TrelloComment> { Comment("act1", "", T0, "M1", isActivity: true) };
        var attachments = Attachments((T0.AddSeconds(-60), "M1", "https://trello.com/1/cards/a/attachments/x.png"));

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        Assert.Equal("", comments[0].Text);
    }

    [Fact]
    public void Foto_dipasangkan_ke_comment_terdekat_kalau_ada_dua_comment()
    {
        var comments = new List<TrelloComment>
        {
            Comment("c1", "pertama", T0.AddMinutes(-8), "M1"),
            Comment("c2", "kedua", T0, "M1"),
        };
        // 2 menit sebelum "kedua" -> jatuh ke c2, bukan c1
        var attachments = Attachments((T0.AddMinutes(-2), "M1", "https://trello.com/1/cards/a/attachments/dekat.png"));

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        Assert.DoesNotContain("![attachment]", comments[0].Text);
        Assert.Contains("![attachment](https://trello.com/1/cards/a/attachments/dekat.png)", comments[1].Text);
    }

    [Fact]
    public void Dua_kali_jalan_sisipan_tidak_berlipat()
    {
        var comments = new List<TrelloComment> { Comment("c1", "tes", T0, "M1") };
        var attachments = Attachments((T0.AddSeconds(-60), "M1", "https://trello.com/1/cards/a/attachments/x.png"));

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);
        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        var occurrences = comments[0].Text.Split("![attachment](").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Comment_kosong_tetap_menampilkan_gambar()
    {
        var comments = new List<TrelloComment> { Comment("c1", "", T0, "M1") };
        var attachments = Attachments((T0.AddSeconds(-60), "M1", "https://trello.com/1/cards/a/attachments/x.png"));

        BaseTrelloListViewModel.LinkAttachmentsToComments(comments, attachments);

        Assert.Equal("![attachment](https://trello.com/1/cards/a/attachments/x.png)", comments[0].Text);
    }
}
