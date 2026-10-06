using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RpaVersionControl.App.Converters;
using RpaVersionControl.App.ViewModels;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Views;

public sealed partial class QaReviewPage : Page
{
    private readonly List<QaQuestionItem> _questions =
        QaChecklistQuestions.All.Select(x => new QaQuestionItem(x.Id, x.Text)).ToList();

    private ChangeRequest? _change;

    public QaReviewPage()
    {
        InitializeComponent();
        QuestionsList.ItemsSource = _questions;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            if (!await services.Security.IsQaAsync(layout))
                throw new UnauthorizedAccessException("Seu usuário Windows não está autorizado como QA.");

            var changeId = (string)e.Parameter;
            _change = await services.Changes.GetAsync(layout, changeId)
                ?? throw new InvalidOperationException("Alteração não encontrada.");

            IdText.Text = _change.Id;
            ProjectText.Text = $"{_change.ProjectName} • revisão {_change.CurrentRevisionNumber}";
            StatusInfo.Message = ChangeStatusToLabelConverter.Describe(_change.Status);
            DeveloperText.Text = $"DEV: {_change.DeveloperDisplayName} ({_change.DeveloperWindowsUser})";
            IncidentText.Text = $"Incidente / solicitação: {_change.IncidentReference}";
            ReasonText.Text = $"Motivo: {_change.Reason}";
            SummaryText.Text = $"Alterado: {_change.ChangeSummary}";
            ImpactText.Text = $"Impacto esperado: {_change.ExpectedImpact}";

            var revision = _change.CurrentRevision;
            FilesList.ItemsSource = revision?.ChangedFiles;

            if (revision is not null && !string.IsNullOrWhiteSpace(revision.DiffRelativePath))
            {
                var diffPath = Path.Combine(layout.Root, revision.DiffRelativePath);
                if (File.Exists(diffPath))
                    Diff.LoadPatch(await File.ReadAllTextAsync(diffPath));
            }

            var retry = _change.Status is ChangeStatus.PublishFailed or ChangeStatus.ApprovedPendingPublish;
            ChecklistPanel.Visibility = retry ? Visibility.Collapsed : Visibility.Visible;
            RetryPublishButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            StatusInfo.Severity = _change.Status == ChangeStatus.PublishFailed
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Informational;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) =>
        await ReviewAsync(approve: true);

    private async void Return_Click(object sender, RoutedEventArgs e) =>
        await ReviewAsync(approve: false);

    private async Task ReviewAsync(bool approve)
    {
        if (_change is null) return;

        try
        {
            if (_questions.Any(x => !x.IsAnswered))
                throw new InvalidOperationException("Responda as 6 perguntas antes de concluir a revisão.");

            if (!approve)
            {
                foreach (var question in _questions.Where(x => !x.IsCompliant))
                {
                    if (string.IsNullOrWhiteSpace(question.Comment))
                        throw new InvalidOperationException($"Informe o comentário da pergunta {question.Id}.");
                }
            }

            var answers = _questions.Select(x => new QaChecklistAnswer
            {
                QuestionId = x.Id,
                Question = x.Question,
                IsCompliant = x.IsCompliant,
                Comment = x.Comment?.Trim() ?? string.Empty
            }).ToList();

            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            var updated = await services.Workflow.ReviewAsync(
                layout, _change.Id, answers, approve, services.Users.GetCurrent());

            var title = updated.Status switch
            {
                ChangeStatus.Published => "Versão publicada",
                ChangeStatus.RetrofitRequired => "Mudança devolvida como possível retrofit",
                ChangeStatus.AdjustmentsRequested => "Ajustes solicitados",
                ChangeStatus.NeedsRebase => "Reenvio necessário",
                _ => "Revisão concluída"
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = updated.Status == ChangeStatus.Published
                    ? $"{updated.Id} publicada como v{updated.PublishedVersion}."
                    : "O DEV receberá o retorno ao abrir o aplicativo.",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
            Frame.Navigate(typeof(QaQueuePage));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void RetryPublish_Click(object sender, RoutedEventArgs e)
    {
        if (_change is null) return;

        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            var version = await services.Workflow.RetryPublishAsync(
                layout, _change.Id, services.Users.GetCurrent());

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Publicação concluída",
                Content = $"Versão v{version.Version} validada em produção.",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
            Frame.Navigate(typeof(QaQueuePage));
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
