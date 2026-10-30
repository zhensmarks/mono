using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models
{
    public partial class TrelloChecklistItem : ObservableObject
    {
        public string Id { get; set; } = "";

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private bool isChecked;

        public string State // API uses "complete" or "incomplete"
        {
            get => IsChecked ? "complete" : "incomplete";
            set => IsChecked = value == "complete";
        }
    }

    /// <summary>
    /// Satu checklist milik kartu Trello. Observable supaya panel checklist bisa
    /// di-sync di tempat (tanpa Clear + re-add) saat ada perubahan dari Trello.
    /// </summary>
    public partial class TrelloChecklist : ObservableObject
    {
        public string Id { get; set; } = "";

        [ObservableProperty]
        private string _name = "";

        public string IdCard { get; set; } = "";

        /// <summary>ObservableCollection: item baru/hapus langsung tampil tanpa reload panel.</summary>
        public ObservableCollection<TrelloChecklistItem> Items { get; set; } = new();

        public int TotalCount => Items.Count;
        public int CompletedCount
        {
            get
            {
                var done = 0;
                foreach (var item in Items) if (item.IsChecked) done++;
                return done;
            }
        }

        /// <summary>Ringkasan progres untuk header checklist (mis. "3/5").</summary>
        public string ProgressText => $"{CompletedCount}/{TotalCount}";

        public void RefreshProgress()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(ProgressText));
        }
    }
}
