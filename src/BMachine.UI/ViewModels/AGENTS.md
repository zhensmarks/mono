# AGENTS.md — BMachine.UI/ViewModels

## ToolboxViewModel.cs

- No Unicode, no emoji, no icons — anywhere in this file.
- Four-piece pattern per tab: enum value + VM property + Is*Visible property +
  OnPropertyChanged call in OnCurrentTabChanged. Add/remove all four together.
- No business logic beyond this pattern.

## MantraDataViewModel.cs

Source of truth. After editing this file, apply the same change to
`BDater/src/BDater/ViewModels/MainViewModel.cs` in the same task.
