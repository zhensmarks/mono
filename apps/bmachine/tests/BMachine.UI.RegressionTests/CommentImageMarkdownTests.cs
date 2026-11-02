using BMachine.UI.Models;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Panel comment hanya merender isi asli comment dari Trello. Gambar tampil hanya bila
/// markdown-nya MEMANG ada di teks comment (disisipkan SendComment saat upload gambar);
/// gambar yang hanya jadi attachment TIDAK pernah disisipkan ke bubble → tidak tampil dobel.
/// </summary>
public class CommentImageMarkdownTests
{
    private sealed class ProbeVm : BaseTrelloListViewModel
    {
        public ProbeVm() : base(new FakeDatabase()) { }
    }

    [Fact]
    public void AppendImageMarkdown_pakai_nama_file_sebagai_alt()
    {
        var url = "https://trello.com/1/cards/abc/attachments/ATT1/foto.png";

        Assert.Equal($"![foto.png]({url})",
            BaseTrelloListViewModel.AppendImageMarkdown("", url, "foto.png"));
        Assert.Equal($"hasil\n\n![foto.png]({url})",
            BaseTrelloListViewModel.AppendImageMarkdown("hasil", url, "foto.png"));
        Assert.Equal($"![image]({url})",
            BaseTrelloListViewModel.AppendImageMarkdown("  ", url, ""));
    }

    [Fact]
    public void BeginEdit_mempertahankan_markdown_gambar_yang_ada_di_comment()
    {
        var vm = new ProbeVm();
        var comment = new TrelloComment
        {
            IsMine = true,
            Text = "cek hasil\n\n![foto.png](https://trello.com/1/cards/abc/attachments/ATT1/foto.png)"
        };

        vm.BeginEditComment(comment);

        // Tidak ada strip sisipan loader lagi — markdown gambar yang asli dari Trello
        // harus selamat saat edit supaya tidak hilang dari comment.
        Assert.Equal(comment.Text, comment.EditText);
    }
}
