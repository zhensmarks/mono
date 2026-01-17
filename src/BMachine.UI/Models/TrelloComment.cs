namespace BMachine.UI.Models;

public class TrelloComment
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string MemberCreatorId { get; set; } = "";
    public string MemberCreatorName { get; set; } = "";
    public string MemberCreatorAvatarUrl { get; set; } = ""; // We might compute this or get from API
    public string MemberCreatorInitials { get; set; } = "";
    public DateTime Date { get; set; }
}
