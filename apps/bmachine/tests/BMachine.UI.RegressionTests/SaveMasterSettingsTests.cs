using System;
using System.IO;
using System.Text.Json;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Pengaturan SAVE MASTER (tab di Script Manager) menulis satu file yang dibaca
/// save_master.jsx. Perubahan formatnya harus disengaja, bukan tidak sengaja.
/// </summary>
public class SaveMasterSettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "bmachine_save_master_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void DefaultsEnableEveryStepWhenConfigMissing()
    {
        var vm = new SaveMasterSettingsViewModel();
        vm.Load(TempPath());

        Assert.True(vm.AntiRamijud);
        Assert.True(vm.RedaksiCheck);
        Assert.True(vm.CloseAfterSave);
        Assert.True(vm.Crop2x3);
        Assert.True(vm.Crop4x6);
        Assert.True(vm.BatchAllDocs);
        Assert.Equal(SaveMasterSettingsViewModel.DefaultRedaksi, vm.RedaksiText);
    }

    [Fact]
    public void SaveThenLoadRoundTripsStepsAndRedaksi()
    {
        string path = TempPath();
        try
        {
            var save = new SaveMasterSettingsViewModel
            {
                AntiRamijud = false,
                RedaksiCheck = true,
                CloseAfterSave = false,
                Crop2x3 = false,
                Crop4x6 = true,
                BatchAllDocs = false,
                RedaksiText = "TK DELAPAN MATA AIR\n  JL. SARI ENDAH NO. 7A  \n\n"
            };
            save.SaveTo(path);
            Assert.True(File.Exists(path));

            var load = new SaveMasterSettingsViewModel();
            load.Load(path);

            Assert.False(load.AntiRamijud);
            Assert.True(load.RedaksiCheck);
            Assert.False(load.CloseAfterSave);
            Assert.False(load.Crop2x3);
            Assert.True(load.Crop4x6);
            Assert.False(load.BatchAllDocs);
            // Baris kosong dibuang, spasi pinggir dipangkas.
            Assert.Equal("TK DELAPAN MATA AIR\nJL. SARI ENDAH NO. 7A", load.RedaksiText);
            Assert.Equal(2, load.RedaksiLines.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SavedJsonUsesKeysTheJsxReads()
    {
        string path = TempPath();
        try
        {
            var vm = new SaveMasterSettingsViewModel
            {
                AntiRamijud = false,
                RedaksiText = "SEKOLAH LAMA"
            };
            vm.SaveTo(path);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.False(root.GetProperty("steps").GetProperty("antiRamijud").GetBoolean());
            Assert.True(root.GetProperty("steps").GetProperty("redaksiCheck").GetBoolean());
            Assert.Equal("SEKOLAH LAMA", root.GetProperty("redaksi")[0].GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EmptyRedaksiIsReportedAndSavedAsEmptyArray()
    {
        string path = TempPath();
        try
        {
            var vm = new SaveMasterSettingsViewModel { RedaksiText = "   \n  \n" };
            vm.SaveTo(path);
            Assert.Equal("Belum ada kalimat terlarang.", vm.RedaksiSummary);
            Assert.Empty(vm.RedaksiLines);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Empty(doc.RootElement.GetProperty("redaksi").EnumerateArray());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void BrokenConfigFileDoesNotThrow()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "{ rusak ");
            var vm = new SaveMasterSettingsViewModel();
            vm.Load(path);
            Assert.True(vm.AntiRamijud);   // jatuh ke bawaan, tidak rekursif error
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveMasterJsxHonoursTheSameConfigFileAndSteps()
    {
        var jsx = ReadRepoFile("apps/bmachine/Scripts/Action/save_master.jsx");

        Assert.Contains(SaveMasterSettingsViewModel.ConfigFileName, jsx);
        Assert.Contains("function stepOn(name)", jsx);
        Assert.Contains("stepOn(\"antiRamijud\")", jsx);
        Assert.Contains("stepOn(\"redaksiCheck\")", jsx);
        Assert.Contains("stepOn(\"closeAfterSave\")", jsx);
        Assert.Contains("stepOn(\"crop2x3\")", jsx);
        Assert.Contains("stepOn(\"crop4x6\")", jsx);
        Assert.Contains("stepOn(\"batchAllDocs\")", jsx);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new FileNotFoundException($"{relativePath} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}
