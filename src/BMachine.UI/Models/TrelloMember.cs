namespace BMachine.UI.Models;

/// <summary>
/// Represents a Trello board member for @mention autocomplete.
/// </summary>
public class TrelloMember
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Initials { get; set; } = "";

    /// <summary>Display text for the mention popup: "Full Name (@username)"</summary>
    public string DisplayText => $"{FullName} (@{Username})";
}
