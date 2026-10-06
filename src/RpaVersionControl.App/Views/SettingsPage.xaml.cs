using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RpaVersionControl.Core.Models;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Views;

public sealed partial class SettingsPage : Page, IRefreshable
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var services = App.Current.Services;
        var user = services.Users.GetCurrent();
        IdentityText.Text = $"{user.DisplayName} • {user.WindowsUser} • {user.MachineName}";

        var local = await services.LocalSettings.GetAsync();
        RootBox.Text = await services.LocalSettings.GetEffectiveSharedRootAsync() ?? local.SharedRootPath;
        StartWithWindowsToggle.IsOn = local.StartWithWindows;
        StartMinimizedToggle.IsOn = local.StartMinimized;
        PollSecondsBox.Value = local.PollSeconds;

        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null)
        {
            RoleText.Text = "Sem conexão compartilhada configurada";
            SecurityCard.Visibility = Visibility.Collapsed;
            return;
        }

        await services.Security.BootstrapIfMissingAsync(layout);
        var isAdmin = await services.Security.IsAdminAsync(layout);
        var isQa = await services.Security.IsQaAsync(layout);
        RoleText.Text = isAdmin ? "Administrador / QA" : isQa ? "QA" : "Desenvolvedor";
        SecurityCard.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        if (isAdmin)
        {
            var security = await services.Security.GetAsync(layout);
            QaUsersBox.Text = string.Join(Environment.NewLine, security.QaUsers);
            QaGroupsBox.Text = string.Join(Environment.NewLine, security.QaWindowsGroups);
            AdminUsersBox.Text = string.Join(Environment.NewLine, security.AdminUsers);
        }
    }

    private async void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var picked = await App.Current.Services.Picker!.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
            RootBox.Text = picked;
    }

    private async void SaveRoot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(RootBox.Text))
                throw new InvalidOperationException("Selecione a pasta compartilhada.");

            var services = App.Current.Services;
            var layout = new SharedLayout(RootBox.Text.Trim());
            layout.Ensure();

            // Explicit write test catches read-only network paths before the user starts versioning.
            var probe = Path.Combine(layout.Config, $".write-test-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "ok");
            File.Delete(probe);

            var local = await services.LocalSettings.GetAsync();
            local.SharedRootPath = layout.Root;
            await services.LocalSettings.SaveAsync(local);
            await services.Security.BootstrapIfMissingAsync(layout);

            RootInfo.Severity = InfoBarSeverity.Success;
            RootInfo.Message = "Conexão validada. Os dados de versionamento serão compartilhados nesta pasta.";
            RootInfo.IsOpen = true;

            await App.Current.MainWindow!.RefreshSecurityUiAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            RootInfo.Severity = InfoBarSeverity.Error;
            RootInfo.Message = ex.Message;
            RootInfo.IsOpen = true;
        }
    }

    private async void SaveLocal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var services = App.Current.Services;
            var settings = await services.LocalSettings.GetAsync();
            settings.StartWithWindows = StartWithWindowsToggle.IsOn;
            settings.StartMinimized = StartMinimizedToggle.IsOn;
            var pollSeconds = double.IsNaN(PollSecondsBox.Value) ? 45 : PollSecondsBox.Value;
            settings.PollSeconds = (int)Math.Clamp(pollSeconds, 15, 300);

            services.Startup.SetEnabled(settings.StartWithWindows);
            await services.LocalSettings.SaveAsync(settings);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Preferências salvas",
                Content = "As novas preferências foram aplicadas.",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async void SaveSecurity_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var services = App.Current.Services;
            var layout = await services.RootProvider.GetRequiredAsync();

            if (!await services.Security.IsAdminAsync(layout))
                throw new UnauthorizedAccessException("Somente administradores podem alterar os acessos.");

            var config = new SecurityConfiguration
            {
                QaUsers = SplitLines(QaUsersBox.Text),
                QaWindowsGroups = SplitLines(QaGroupsBox.Text),
                AdminUsers = SplitLines(AdminUsersBox.Text)
            };

            if (config.AdminUsers.Count == 0)
                throw new InvalidOperationException("Mantenha pelo menos um administrador.");

            await services.Security.SaveAsync(layout, config);
            await App.Current.MainWindow!.RefreshSecurityUiAsync();

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Acessos atualizados",
                Content = "As permissões passam a valer na próxima operação.",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private static List<string> SplitLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
