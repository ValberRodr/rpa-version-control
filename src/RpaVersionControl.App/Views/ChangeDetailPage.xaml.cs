using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RpaVersionControl.Core.Models;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Views;

public sealed partial class ChangeDetailPage : Page
{
    private ChangeRequest? _change;

    public ChangeDetailPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            var changeId = (string)e.Parameter;
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            _change = await services.Changes.GetAsync(layout, changeId);
            if (_change is null) return;

            var user = services.Users.GetCurrent();
            var isOwner = string.Equals(_change.DeveloperWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !await services.Security.IsQaAsync(layout))
                throw new UnauthorizedAccessException("Você não tem permissão para visualizar esta alteração.");

            await BindChangeAsync(layout, _change);
        }
        catch (Exception ex)
        {
            _change = null;
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task BindChangeAsync(SharedLayout layout, ChangeRequest change)
    {
        _change = change;
        IdText.Text = _change.Id;
        ProjectText.Text = _change.ProjectName;
        StatusInfo.Message = _change.Status.ToString();
        StatusInfo.Severity = _change.Status switch
        {
            ChangeStatus.Published => InfoBarSeverity.Success,
            ChangeStatus.AdjustmentsRequested or ChangeStatus.RetrofitRequired or ChangeStatus.NeedsRebase => InfoBarSeverity.Warning,
            ChangeStatus.PublishFailed => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

        IncidentText.Text = $"Incidente / solicitação: {_change.IncidentReference}";
        ReasonText.Text = $"Motivo: {_change.Reason}";
        SummaryText.Text = $"Alterado: {_change.ChangeSummary}";
        ImpactText.Text = $"Impacto esperado: {_change.ExpectedImpact}";

        var checklist = _change.CurrentRevision?.Checklist;
        var noAnswers = checklist?.Answers.Where(x => !x.IsCompliant).ToList() ?? new();
        QaAnswersList.ItemsSource = noAnswers;
        NoQaReturnText.Visibility = noAnswers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ResubmitButton.Visibility = _change.Status is
            ChangeStatus.AdjustmentsRequested or ChangeStatus.RetrofitRequired or ChangeStatus.NeedsRebase
            ? Visibility.Visible
            : Visibility.Collapsed;

        var revision = _change.CurrentRevision;
        if (revision is not null)
        {
            var diffPath = Path.Combine(layout.Root, revision.DiffRelativePath);
            if (File.Exists(diffPath))
                Diff.LoadPatch(await File.ReadAllTextAsync(diffPath));
        }
    }

    private void Resubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_change is not null)
            (App.Current.MainWindow)?.NavigateToSubmission(_change.ProjectId, _change.Id);
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
