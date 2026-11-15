using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class PrivacyView : UserControl
{
    private List<PrivacySetting> _settings = [];

    public PrivacyView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _settings = PrivacyService.Load();
        SettingsList.ItemsSource = null;
        SettingsList.ItemsSource = _settings;
        StatusText.Text = $"{_settings.Count(s => s.Enabled)} {Loc.T("de")} {_settings.Count} {Loc.T("ajustes protegidos")}";
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PrivacySetting s } box) return;
        bool want = box.IsChecked == true;
        if (!PrivacyService.Set(s, want))
            Dialogs.Show(Loc.T("No se pudo cambiar este ajuste."), "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
        Reload();
    }

    private void ApplyRecommended_Click(object sender, RoutedEventArgs e)
    {
        var todo = _settings.Where(s => s.Recommended && !s.Enabled).ToList();
        if (todo.Count == 0) { Dialogs.Toast(Loc.T("Ya están aplicados todos los ajustes recomendados")); return; }
        int ok = todo.Count(s => PrivacyService.Set(s, true));
        Reload();
        Dialogs.Toast($"{ok} {Loc.T("ajustes de privacidad aplicados")}");
    }

    private void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var active = _settings.Where(s => s.Enabled).ToList();
        if (active.Count == 0) return;
        if (Dialogs.Show($"{Loc.T("¿Restaurar los valores originales de")} {active.Count} {Loc.T("ajustes?")}", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int ok = active.Count(s => PrivacyService.Set(s, false));
        Reload();
        Dialogs.Toast($"{ok} {Loc.T("ajustes restaurados")}");
    }
}
