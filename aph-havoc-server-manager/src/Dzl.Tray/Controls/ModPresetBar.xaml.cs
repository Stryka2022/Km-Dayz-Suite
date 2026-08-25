using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>Mod-preset (loadout) bar: auto-apply combo + inline save-as + update/delete menu.
/// DataContext = MainViewModel (inherited). Used on the ServerEditorWindow Mods tab and the
/// Mods page header.</summary>
public partial class ModPresetBar : UserControl
{
    public ModPresetBar() => InitializeComponent();
}
