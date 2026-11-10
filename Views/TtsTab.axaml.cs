using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter.Views;

public partial class TtsTab : UserControl
{
    public TtsTab()
    {
        InitializeComponent();
    }

    private void OnWordReplacementsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual visual)
        {
            var item = visual.FindAncestorOfType<ListBoxItem>()?.DataContext as WordReplacementItemViewModel;
            if (item != null && DataContext is TtsTabViewModel vm)
            {
                vm.PopulateWordReplacementInputs(item);
            }
        }
    }

    private void OnCharReplacementsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual visual)
        {
            var item = visual.FindAncestorOfType<ListBoxItem>()?.DataContext as WordReplacementItemViewModel;
            if (item != null && DataContext is TtsTabViewModel vm)
            {
                vm.PopulateCharReplacementInputs(item);
            }
        }
    }
}