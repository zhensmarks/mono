using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models
{
    public partial class TrelloChecklistItem : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        
        [ObservableProperty]
        private bool isChecked;
        
        public string State // API uses "complete" or "incomplete"
        {
            get => IsChecked ? "complete" : "incomplete";
            set => IsChecked = value == "complete";
        } 
    }

    public class TrelloChecklist
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string IdCard { get; set; } = "";
        public List<TrelloChecklistItem> Items { get; set; } = new();
    }
}
