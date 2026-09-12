using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models.MantraData;

public sealed class SeparatorOption
{
    public string Label { get; }
    public string Value { get; }

    public SeparatorOption(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public override string ToString() => Label;
}

public partial class LayerItemView : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _defaultSeparator = "\n";

    public ObservableCollection<ColumnEntryItem> Entries { get; } = new();

    public LayerItemView(string name)
    {
        _name = name;
    }
}

public partial class ColumnEntryItem : ObservableObject
{
    private static readonly string[] _prefixOptions =
    {
        "", "NISN : ", "NIS : ", "NAMA : ", "Jabatan : ", "TTL : ", "Alamat : ", "RT ", "RW ", "No. "
    };

    private static readonly string[] _separatorOptions =
    {
        "<default>", "Baris baru", "Spasi", "Koma + spasi", "Garis miring", "Strip", "Tanpa pemisah"
    };

    public int SourceIndex { get; }
    public string Header { get; }

    public IReadOnlyList<string> PrefixOptions => _prefixOptions;
    public IReadOnlyList<string> SeparatorOptions => _separatorOptions;

    [ObservableProperty]
    private bool _isUsed;

    [ObservableProperty]
    private string _prefix = "";

    [ObservableProperty]
    private string _separator = "<default>";

    public ColumnEntryItem(int sourceIndex, string header)
    {
        SourceIndex = sourceIndex;
        Header = header;
    }
}

public class LayerTransformEntry
{
    public int SourceIndex { get; set; }
    public string Header { get; set; } = "";
    public bool Use { get; set; }
    public string Prefix { get; set; } = "";
    public string Separator { get; set; } = "<default>";
}

public class LayerTransformSpec
{
    public string Name { get; set; } = "";
    public string Separator { get; set; } = "\n";
    public List<LayerTransformEntry> Entries { get; set; } = new();
}