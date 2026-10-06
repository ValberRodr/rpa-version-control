using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RpaVersionControl.App.Controls;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Views;

public sealed partial class HistoryPage : Page, IRefreshable
{
    private string? _requestedProjectId;
    private ProjectDefinition? _selectedProject;

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _requestedProjectId = e.Parameter as string;
    }

    public async Task RefreshAsync()
    {
        var services = App.Current.Services;
        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null) return;

        var projects = await services.Projects.ListAsync(layout);
        ProjectCombo.ItemsSource = projects;

        if (projects.Count == 0)
        {
            VersionsList.ItemsSource = Array.Empty<VersionRecord>();
            return;
        }

        if (_selectedProject is null)
        {
            var index = !string.IsNullOrWhiteSpace(_requestedProjectId)
                ? projects.ToList().FindIndex(x => x.Id == _requestedProjectId)
                : 0;
            ProjectCombo.SelectedIndex = Math.Max(index, 0);
        }
        else
        {
            ProjectCombo.SelectedItem = projects.FirstOrDefault(x => x.Id == _selectedProject.Id);
        }
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProject = ProjectCombo.SelectedItem as ProjectDefinition;
        if (_selectedProject is null) return;

        var services = App.Current.Services;
        var layout = await services.RootProvider.GetRequiredAsync();
        VersionsList.ItemsSource = await services.Projects.ListVersionsAsync(layout, _selectedProject.Id);
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || sender is not Button { Tag: int versionNumber }) return;

        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            var version = await services.Json.ReadAsync<VersionRecord>(
                layout.VersionFile(_selectedProject.Id, versionNumber))
                ?? throw new InvalidOperationException("Versão não encontrada.");

            var current = await services.Projects.GetAsync(layout, _selectedProject.Id)
                ?? throw new InvalidOperationException("Projeto não encontrado.");

            var patch = await services.Git.CompareCommitsAsync(
                current, version.CommitSha, current.CurrentCommitSha);

            var viewer = new DiffViewer { Height = 620, Width = 980 };
            viewer.LoadPatch(patch);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"v{versionNumber} → v{current.CurrentVersion}",
                Content = viewer,
                CloseButtonText = "Fechar"
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || sender is not Button { Tag: int versionNumber }) return;

        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            if (!await services.Security.IsQaAsync(layout))
                throw new UnauthorizedAccessException("Somente usuários QA podem restaurar versões.");

            var project = await services.Projects.GetAsync(layout, _selectedProject.Id)
                ?? throw new InvalidOperationException("Projeto não encontrado.");
            if (versionNumber == project.CurrentVersion)
                throw new InvalidOperationException("Esta já é a versão atual.");

            var reason = new TextBox
            {
                Header = "Motivo do rollback",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 100
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Restaurar conteúdo da v{versionNumber}?",
                Content = reason,
                PrimaryButtonText = "Restaurar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var result = await services.Workflow.RollbackAsync(
                layout, project.Id, versionNumber, reason.Text, services.Users.GetCurrent());

            await ShowInfoAsync($"Rollback publicado como v{result.Version}.");
            _selectedProject = null;
            _requestedProjectId = project.Id;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void CheckIntegrity_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            await ShowErrorAsync("Selecione um projeto.");
            return;
        }

        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            if (!await services.Security.IsQaAsync(layout))
                throw new UnauthorizedAccessException("Somente usuários QA podem verificar a integridade da produção.");

            var report = await services.Workflow.BuildDriftReportAsync(layout, _selectedProject.Id);

            var panel = new StackPanel { Spacing = 12, Width = 620 };

            if (!report.HasDrift)
            {
                panel.Children.Add(new InfoBar
                {
                    IsOpen = true,
                    Severity = InfoBarSeverity.Success,
                    Message = $"Produção corresponde à versão aprovada v{report.ApprovedVersion}. Nenhuma edição não autorizada detectada."
                });
            }
            else
            {
                panel.Children.Add(new InfoBar
                {
                    IsOpen = true,
                    Severity = InfoBarSeverity.Error,
                    Title = "Integridade COMPROMETIDA",
                    Message = $"{report.Files.Count} arquivo(s) foram alterados em produção fora do fluxo de aprovação " +
                              $"(fora da v{report.ApprovedVersion}). Esses arquivos não são confiáveis até serem restaurados ou reaprovados."
                });

                var filesPanel = new StackPanel { Spacing = 4 };
                foreach (var file in report.Files)
                {
                    var modified = file.LastWriteTimeUtc is { } ts ? ts.ToLocalTime().ToString("g") : "desconhecido";
                    filesPanel.Children.Add(new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"• {file.Path} [{file.Kind}] — editor suspeito: {file.SuspectedEditor ?? "desconhecido"} " +
                               $"— modificado em: {modified}"
                    });
                }
                panel.Children.Add(filesPanel);

                var diff = new DiffViewer { Height = 320 };
                diff.LoadPatch(report.Patch);
                panel.Children.Add(diff);

                panel.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Text = "O \"editor suspeito\" é o proprietário NTFS do arquivo (melhor esforço) — pode não refletir " +
                           "exatamente quem fez a última edição, apenas uma pista para investigação.",
                    Opacity = 0.8
                });
            }

            var reasonBox = new TextBox
            {
                Header = "Motivo da restauração (obrigatório para restaurar)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80
            };
            if (report.HasDrift)
                panel.Children.Add(reasonBox);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Integridade da produção — {report.ProjectName}",
                Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
                PrimaryButtonText = report.HasDrift ? "Restaurar produção para a versão aprovada" : null,
                CloseButtonText = "Fechar",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            if (string.IsNullOrWhiteSpace(reasonBox.Text))
                throw new InvalidOperationException("Informe o motivo da restauração.");

            await services.Workflow.RestoreProductionToApprovedAsync(
                layout, _selectedProject.Id, reasonBox.Text, services.Users.GetCurrent());

            await ShowInfoAsync("Produção restaurada para a versão aprovada. A edição não autorizada foi sobrescrita.");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Concluído",
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
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
