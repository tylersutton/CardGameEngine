using Core;
using Godot;

/// The game's "status" lines beside the table, under the "show_variables" (with designer info, every variable).
public partial class StatusView : Label
{
    Game? game;
    bool designerInfo;

    public void ShowGame(Game gameToShow)
    {
        game = gameToShow;
        game.VariablesChanged += Refresh;
        game.StateChanged += Refresh;
        Refresh();
    }

    public void ShowDesignerInfo(bool shown)
    {
        designerInfo = shown;
        if (game != null)
        {
            Refresh();
        }
    }

    void Refresh()
    {
        Dictionary<string, string> values = [];
        IEnumerable<string> shown = designerInfo ? game!.Variables.Keys : game!.Rules.ShowVariables.Select(Values.Text);
        foreach (string name in shown)
        {
            values[name] = Values.Format(game.Variables.GetValueOrDefault(name));
        }
        foreach ((string label, string value) in game.StatusValues())
        {
            values[label] = value;
        }
        Visible = values.Count > 0;
        Text = string.Join("\n", values.Select(pair => $"{pair.Key}: {pair.Value}"));
    }
}
