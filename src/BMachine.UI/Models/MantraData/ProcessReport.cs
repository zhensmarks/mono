using System.IO;

namespace BMachine.UI.Models.MantraData;

public class PhotoMatchResult
{
    public string StudentName { get; set; } = string.Empty;
    public string? MatchedFilePath { get; set; }
    public string MatchedFileName => !string.IsNullOrEmpty(MatchedFilePath) ? Path.GetFileName(MatchedFilePath) : "-";
    public int Score { get; set; }
    public bool IsPassed { get; set; }
    public string BestCandidateName { get; set; } = string.Empty;
    public string BestCandidatePath { get; set; } = string.Empty;
    public bool IsForced { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public class ProcessReportItem
{
    public int No { get; set; }
    public string Slot { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Foto { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public bool PhotoOk { get; set; }
    public bool TextOk { get; set; }
    public int MatchScore { get; set; }
    public string Info { get; set; } = string.Empty;
}

public class ProcessReportPage
{
    public string Psd { get; set; } = string.Empty;
    public int Filled { get; set; }
    public int Slots { get; set; }
    public bool SkippedNoData { get; set; }
    public System.Collections.Generic.List<ProcessReportItem> Items { get; set; } = new();
}

public class ProcessReport
{
    public bool Done { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public int PhotosTotal { get; set; }
    public int PsdFilesTotal { get; set; }
    public int RowsTotal { get; set; }
    public int RowsProcessed { get; set; }
    public int RowsSkipped { get; set; }
    public int RowsAmbiguous { get; set; }
    public int RowsDuplicate { get; set; }
    public System.Collections.Generic.List<ProcessReportPage> Pages { get; set; } = new();
    public System.Collections.Generic.List<string> Errors { get; set; } = new();
}
