using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Views;

public sealed partial class ProjectsPage : Page, IRefreshable
{
    public ProjectsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var services = App.Current.Services;
        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null) return;

        ProjectsList.ItemsSource = await services.Projects.ListAsync(layout);
        NewProjectButton.Visibility = await services.Security.IsAdminAsync(layout)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();
            if (!await services.Security.IsAdminAsync(layout))
                throw new UnauthorizedAccessException("Somente administradores podem cadastrar projetos.");

            var name = new TextBox { Header = "Nome do projeto", PlaceholderText = "Ex.: RPA Sinistro Auto" };
            var folder = new TextBox { Header = "Pasta oficial", IsReadOnly = true, PlaceholderText = @"\\rede\RPAs\SinistroAuto" };
            var browse = new Button { Content = "Selecionar pasta" };
            browse.Click += async (_, _) =>
            {
                var picked = await services.Picker!.PickFolderAsync();
                if (!string.IsNullOrWhiteSpace(picked))
                    folder.Text = picked;
            };

            var ignore = new TextBox
            {
                Header = "Ignorar no versionamento — uma regra por linha",
                Text = string.Join(Environment.NewLine, IgnoreMatcher.DefaultPatterns),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 160
            };

            var info = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                IsOpen = true,
                Message = "Logs, temporários e caches são ignorados. O app nunca apaga arquivos ignorados durante publicação ou rollback."
            };

            var panel = new StackPanel { Spacing = 12, Width = 620 };
            panel.Children.Add(name);
            panel.Children.Add(folder);
            panel.Children.Add(browse);
            panel.Children.Add(ignore);
            panel.Children.Add(info);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Cadastrar projeto",
                Content = panel,
                PrimaryButtonText = "Criar baseline",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var patterns = ignore.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            await services.Workflow.CreateProjectAsync(
                layout, name.Text, folder.Text, patterns, services.Users.GetCurrent());

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private void NewVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string projectId })
            (App.Current.MainWindow)?.NavigateToSubmission(projectId);
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string projectId })
            (App.Current.MainWindow)?.NavigateToHistory(projectId);
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
