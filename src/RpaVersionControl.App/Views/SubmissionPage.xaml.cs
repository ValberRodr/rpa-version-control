using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RpaVersionControl.Core.Models;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Views;

public sealed partial class SubmissionPage : Page
{
    private string _projectId = string.Empty;
    private string? _existingChangeId;
    private SubmissionPreview? _preview;

    public SubmissionPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var args = (SubmissionNavigationArgs)e.Parameter;
        _projectId = args.ProjectId;
        _existingChangeId = args.ExistingChangeId;

        var services = App.Current.Services;
        var layout = await services.RootProvider.GetRequiredAsync();
        var project = await services.Projects.GetAsync(layout, _projectId);
        ProjectText.Text = $"{project?.Name} • versão oficial v{project?.CurrentVersion}";

        if (!string.IsNullOrWhiteSpace(_existingChangeId))
        {
            TitleText.Text = "Corrigir e reenviar";
            var change = await services.Changes.GetAsync(layout, _existingChangeId);
            if (change is not null)
            {
                IncidentBox.Text = change.IncidentReference;
                ReasonBox.Text = change.Reason;
                SummaryBox.Text = change.ChangeSummary;
                ImpactBox.Text = change.ExpectedImpact;
            }
        }
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picked = await App.Current.Services.Picker!.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            FolderBox.Text = picked;
            _preview = null;
            SubmitButton.IsEnabled = false;
            PreviewInfo.IsOpen = false;
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FolderBox.Text))
                throw new InvalidOperationException("Selecione a pasta candidata.");

            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();

            _preview = await services.Workflow.PreviewSubmissionAsync(
                layout, _projectId, FolderBox.Text);

            FilesList.ItemsSource = _preview.Changes;
            Diff.LoadPatch(_preview.Patch);

            if (_preview.Changes.Count == 0)
            {
                PreviewInfo.Severity = InfoBarSeverity.Warning;
                PreviewInfo.Message = "Nenhuma diferença foi encontrada em relação à versão oficial.";
                SubmitButton.IsEnabled = false;
            }
            else
            {
                PreviewInfo.Severity = InfoBarSeverity.Success;
                PreviewInfo.Message = $"{_preview.Changes.Count} arquivo(s) com alteração. Revise o diff antes de submeter.";
                SubmitButton.IsEnabled = true;
            }

            PreviewInfo.IsOpen = true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_preview is null)
                throw new InvalidOperationException("Gere a prévia antes de submeter.");

            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();

            var change = await services.Workflow.SubmitAsync(
                layout,
                _preview,
                IncidentBox.Text,
                ReasonBox.Text,
                SummaryBox.Text,
                ImpactBox.Text,
                services.Users.GetCurrent(),
                _existingChangeId);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"{change.Id} submetida",
                Content = $"Revisão {change.CurrentRevisionNumber} enviada para a fila de QA.",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();

            Frame.Navigate(typeof(MyChangesPage));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Não foi possível concluir",
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}
