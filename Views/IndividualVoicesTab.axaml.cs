using System.Linq;
using Avalonia.Controls;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter.Views;

public partial class IndividualVoicesTab : UserControl
{
    public IndividualVoicesTab()
    {
        InitializeComponent();
    }

    private void VoiceComboBox_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        if (comboBox.DataContext
            is not IndividualVoiceAssignment assignment)
            return;

        if (comboBox.SelectedItem
            is not IndividualVoiceSettings voice)
            return;

        if (DataContext
            is not IndividualVoicesTabViewModel viewModel)
            return;

        viewModel.ChangeAssignmentVoice(
            assignment,
            voice.Name);
    }
}