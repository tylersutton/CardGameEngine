using Godot;

/// The toolbar's menu of tools for making a game rather than playing it: reload, sandbox, zone names, plays,
/// designer info, how fast bots move, and a bot to play for you.
public partial class DesignerMenu : MenuButton
{
    const int ReloadId = 0;
    const int SandboxId = 1;
    const int ZoneNamesId = 2;
    const int DesignerInfoId = 3;
    const int ShowPlaysId = 4;
    const int PlaysForMeId = 5;
    /// The bot speeds, by item id: seconds between bot moves.
    static readonly Dictionary<int, (string Text, float Delay)> BotSpeeds = new()
    {
        [10] = ("Slow bots", 1.5f), [11] = ("Normal bots", 0.6f), [12] = ("Fast bots", 0.15f),
    };
    /// Kept between games, as a person's preference.
    static int botSpeedId = 11;

    public event Action? ReloadPressed;
    public event Action<bool>? SandboxToggled;
    public event Action<bool>? ZoneNamesToggled;
    public event Action<bool>? DesignerInfoToggled;
    public event Action<bool>? ShowPlaysToggled;
    public event Action<bool>? PlaysForMeToggled;

    public bool Sandbox => IsChecked(SandboxId);
    public bool PlaysForMe => IsChecked(PlaysForMeId);
    public float BotDelay => BotSpeeds[botSpeedId].Delay;

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
        Items.AddCheckItem("Show plays", ShowPlaysId);
        Items.AddCheckItem("Designer info", DesignerInfoId);
        Items.AddSeparator();
        foreach ((int id, (string text, float _)) in BotSpeeds)
        {
            Items.AddRadioCheckItem(text, id);
            Items.SetItemChecked(Items.GetItemIndex(id), id == botSpeedId);
        }
        Items.AddCheckItem("Bot plays for me", PlaysForMeId);
        SetTooltip(ReloadId, "Start over from the files on disk");
        SetTooltip(SandboxId, "Ignore move rules and action conditions");
        SetTooltip(ZoneNamesId, "Show each zone's name and card count, as used in rules");
        SetTooltip(ShowPlaysId, "Outline every card and zone the player whose turn it is could use now");
        SetTooltip(DesignerInfoId, "Show every variable, and the steps' workings in the log");
        SetTooltip(PlaysForMeId, "A bot takes your turns too: to play a game out, or watch bots play it");
        Items.IdPressed += OnItemPressed;
    }

    /// Inside the editor, which reloads the game itself.
    public void HideReload()
    {
        Items.RemoveItem(Items.GetItemIndex(ReloadId));
    }

    /// Online, everyone plays the host's game by its rules, and makes their own moves.
    public void DisableForOnline()
    {
        Items.SetItemDisabled(Items.GetItemIndex(ReloadId), true);
        Items.SetItemDisabled(Items.GetItemIndex(SandboxId), true);
        Items.SetItemDisabled(Items.GetItemIndex(PlaysForMeId), true);
    }

    void OnItemPressed(long id)
    {
        if (id == ReloadId)
        {
            ReloadPressed?.Invoke();
            return;
        }
        if (BotSpeeds.ContainsKey((int)id))
        {
            botSpeedId = (int)id;
            foreach (int speedId in BotSpeeds.Keys)
            {
                Items.SetItemChecked(Items.GetItemIndex(speedId), speedId == botSpeedId);
            }
            return;
        }
        int index = Items.GetItemIndex((int)id);
        Items.SetItemChecked(index, !Items.IsItemChecked(index));
        bool on = Items.IsItemChecked(index);
        Action<bool>? toggled = id switch
        {
            SandboxId => SandboxToggled,
            ZoneNamesId => ZoneNamesToggled,
            ShowPlaysId => ShowPlaysToggled,
            PlaysForMeId => PlaysForMeToggled,
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
