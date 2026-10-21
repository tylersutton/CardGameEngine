using Core;
using Godot;

/// The variables (or "show_variables") and the game's "status" lines beside the table.
public partial class StatusView : Label
{
    Game? game;

    public void ShowGame(Game gameToShow)
    {
        game = gameToShow;
        game.VariablesChanged += Refresh;
        game.StateChanged += Refresh;
        Refresh();
    }

    void Refresh()
    {
        Dictionary<string, string> values = [];
        IEnumerable<string> shown = game!.Rules.ShowVariables.Count > 0 ? game.Rules.ShowVariables.Select(Values.Text) : game.Variables.Keys;
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
