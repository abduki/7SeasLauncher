using System.Windows;
using System.Windows.Controls;
using SevenSeas.Launcher.ViewModels;

namespace SevenSeas.Launcher.Views;

/// <summary>The Jobs view: active and recently finished downloads.</summary>
public partial class JobsView : UserControl
{
    private readonly JobsViewModel _viewModel;

    public JobsView(JobsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobsViewModel.IsEmpty))
            {
                UpdateEmptyState();
            }
        };

        Loaded += (_, _) =>
        {
            _viewModel.Refresh();
            UpdateEmptyState();
        };
    }

    private void UpdateEmptyState()
        => EmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        UpdateEmptyState();
    }
}
