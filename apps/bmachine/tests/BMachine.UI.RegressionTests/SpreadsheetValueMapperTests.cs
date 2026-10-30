using System.Globalization;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class SpreadsheetValueMapperTests
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    [Fact]
    public void NumericCellsRemainNumericAndUseTheSpreadsheetLocale()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "1.234,50", 12.5d, SpreadsheetCellInputKind.Number, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.Raw, mapped.InputMode);
        Assert.Equal(1234.5d, Assert.IsType<double>(mapped.Value), 4);
    }

    [Fact]
    public void TextCellsKeepLeadingZerosAndDateLikeTextLiteral()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "0012/03", "0007/11", SpreadsheetCellInputKind.Text, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.Raw, mapped.InputMode);
        Assert.Equal("0012/03", Assert.IsType<string>(mapped.Value));
    }

    [Fact]
    public void DateCellsConvertLocaleInputToSheetsSerial()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "31/12/2025", 46000d, SpreadsheetCellInputKind.Date, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.Raw, mapped.InputMode);
        Assert.Equal(new DateTime(2025, 12, 31).ToOADate(), Assert.IsType<double>(mapped.Value));
    }

    [Fact]
    public void BooleanCellsRemainBoolean()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "TRUE", false, SpreadsheetCellInputKind.Boolean, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.Raw, mapped.InputMode);
        Assert.True(Assert.IsType<bool>(mapped.Value));
    }

    [Fact]
    public void FormulasUseSheetsParserButPlainTextDoesNot()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "=SUM(A1:A2)", null, SpreadsheetCellInputKind.Text, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.UserEntered, mapped.InputMode);
        Assert.Equal("=SUM(A1:A2)", mapped.Value);
    }

    [Fact]
    public void EmptyInputClearsCellWithoutChangingInputMode()
    {
        var mapped = SpreadsheetValueMapper.MapForWrite(
            "", 15d, SpreadsheetCellInputKind.Number, Indonesian);

        Assert.Equal(SpreadsheetValueInputMode.Raw, mapped.InputMode);
        Assert.Equal(string.Empty, mapped.Value);
    }

    [Fact]
    public void QuotedSheetNamesEscapeEmbeddedApostrophes()
    {
        Assert.Equal("'Owner''s Data'", SpreadsheetValueMapper.QuoteSheetName("Owner's Data"));
    }

    [Fact]
    public void NewerRefreshInvalidatesOlderResponses()
    {
        var sequence = new SpreadsheetLoadGeneration();
        var oldRequest = sequence.Start();
        var newestRequest = sequence.Start();

        Assert.False(sequence.IsCurrent(oldRequest));
        Assert.True(sequence.IsCurrent(newestRequest));
    }
}
