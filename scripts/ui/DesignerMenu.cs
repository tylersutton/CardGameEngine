using Godot;

/// The toolbar's menu of tools for making a game rather than playing it: reload, sandbox, zone names and designer
/// info.
public partial class DesignerMenu : MenuButton
{
    const int ReloadId = 0;
    const int SandboxId = 1;
    const int ZoneNamesId = 2;
    const int DesignerInfoId = 3;

    public event Action? ReloadPressed;
    public event Action<bool>? SandboxToggled;
    public event Action<bool>? ZoneNamesToggled;
    public event Action<bool>? DesignerInfoToggled;

    public bool Sandbox => IsChecked(SandboxId);

    public bool DesignerInfo
    {
        get => IsChecked(DesignerInfoId);
        set => Items.SetItemChecked(Items.GetItemIndex(DesignerInfoId), value);
    }

    PopupMenu Items => GetPopup();

    public override void _Ready()
    {
        Items.AddItem("Reload", ReloadId);
        Items.AddCheckItem("Sandbox", SandboxId);
        Items.AddCheckItem("Zone names", ZoneNamesId);
        Items.AddCheckItem("Designer info", DesignerInfoId);
        SetTooltip(ReloadId, "Start over from the files on disk");
        SetTooltip(SandboxId, "Ignore move rules and action conditions");
        SetTooltip(ZoneNamesId, "Show each zone's name and card count, as used in rules");
        SetTooltip(DesignerInfoId, "Show every variable, and the steps' workings in the log");
        Items.IdPressed += OnItemPressed;
    }

    /// Inside the editor, which reloads the game itself.
    public void HideReload()
    {
        Items.RemoveItem(Items.GetItemIndex(ReloadId));
    }

    /// Online, everyone plays the host's game by its rules.
    public void DisableReloadAndSandbox()
    {
        Items.SetItemDisabled(Items.GetItemIndex(ReloadId), true);
        Items.SetItemDisabled(Items.GetItemIndex(SandboxId), true);
    }

    void OnItemPressed(long id)
    {
        if (id == ReloadId)
        {
            ReloadPressed?.Invoke();
            return;
        }
        int index = Items.GetItemIndex((int)id);
        Items.SetItemChecked(index, !Items.IsItemChecked(index));
        bool on = Items.IsItemChecked(index);
        Action<bool>? toggled = id switch
        {
            SandboxId => SandboxToggled,
            ZoneNamesId => ZoneNamesToggled,
            _ => DesignerInfoToggled,
        };
        toggled?.Invoke(on);
    }

    bool IsChecked(int id) => Items.IsItemChecked(Items.GetItemIndex(id));

    void SetTooltip(int id, string text)
    {
        Items.SetItemTooltip(Items.GetItemIndex(id), text);
    }
}
