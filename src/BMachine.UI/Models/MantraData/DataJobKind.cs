namespace BMachine.UI.Models.MantraData;

public enum DataJobKind
{
    YearbookStudent,
    YearbookTeacher,
    IdCardStudent,
    IdCardStaff
}

public static class DataJobKindInfo
{
    public static string Label(DataJobKind kind) => kind switch
    {
        DataJobKind.YearbookStudent => "Buku tahunan (siswa)",
        DataJobKind.YearbookTeacher => "Buku tahunan (guru)",
        DataJobKind.IdCardStudent => "ID card (siswa)",
        DataJobKind.IdCardStaff => "ID card (staf / guru)",
        _ => kind.ToString()
    };
}
