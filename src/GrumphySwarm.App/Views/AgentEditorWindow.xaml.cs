using System.Windows;
using System.Windows.Input;
using GrumphySwarm.App.ViewModels;
using GrumphySwarm.Core.Agents;

namespace GrumphySwarm.App.Views;

/// <summary>
/// Lightweight dark editor for agent profiles. Modal-less on purpose: the
/// notch keeps working while this window is open.
/// </summary>
public partial class AgentEditorWindow : Window
{
    private readonly AgentsViewModel _viewModel;
    private readonly AgentProfile? _existing;

    public AgentEditorWindow(AgentsViewModel viewModel, AgentProfile? existing)
    {
        _viewModel = viewModel;
        _existing = existing;
        InitializeComponent();

        Title = existing is null ? "Grumphy — New agent" : "Grumphy — Edit agent";
        NameBox.Text = existing?.Name ?? string.Empty;
        DescriptionBox.Text = existing?.Description ?? string.Empty;

        TaskTypeBox.ItemsSource = AgentTaskTypes.All;
        TaskTypeBox.SelectedValue = existing?.TaskTypeId ?? AgentTaskTypes.All[0].Id;

        CloseButton.Click += (_, _) => Close();
        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => Save();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Loaded += (_, _) => Keyboard.Focus(NameBox);
    }

    private void Save()
    {
        var error = _viewModel.TrySave(
            _existing,
            NameBox.Text,
            TaskTypeBox.SelectedValue as string ?? string.Empty,
            DescriptionBox.Text);

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Close();
    }
}
