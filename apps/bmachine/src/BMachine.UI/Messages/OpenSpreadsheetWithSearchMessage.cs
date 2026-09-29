namespace BMachine.UI.Messages;

public class OpenSpreadsheetWithSearchMessage
{
    public string Value { get; }

    public OpenSpreadsheetWithSearchMessage(string value)
    {
        Value = value;
    }
}
