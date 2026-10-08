using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TtsStreamSupporter.ViewModels;

namespace TtsStreamSupporter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = Program.Services.GetRequiredService<MainWindowViewModel>();
    }
}