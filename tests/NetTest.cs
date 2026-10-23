using Core;
using Godot;

/// Two processes play a hand of Hearts over the network (two people, two bots on the host), then compare. The guest
/// is seated as it joins, asks for an undo partway (the host allows it), drops out and rejoins by name, and finally
/// scribbles on its own copy to check it notices it's out of step and catches up. Then the host takes everyone back to
/// the lobby and starts a second match, which both play with the same seats before comparing.
/// Run both, then check the two printed state numbers match:
///   godot --headless res://tests/net_test.tscn -- host
///   godot --headless res://tests/net_test.tscn -- guest
public partial class NetTest : Node
{
    const int Port = 7790;
    const string GamePath = "res://games/hearts.jsonc";
    const double TimeLimit = 240;
    const int UndoAt = 20;
    const int RejoinAt = 40;
    const int CorruptAt = 60;

    readonly bool hosting = OS.GetCmdlineUserArgs().Contains("host");
    Bot? bot;
    RandomNumbers chooser = new(0);
    int waitingFor = -1;
    bool askedUndo;
    bool rejoined;
    bool corrupted;
    bool backInLobby;
    bool finished;

    Net Net => Net.Instance;

    string Role => hosting ? "host" : "guest";

    public override void _Ready()
    {
        chooser = new(hosting ? 7UL : 8UL);
        Net.MatchStarted += OnMatchStarted;
        Net.MatchEnded += () => backInLobby = true;
        Net.Notice += text => GD.Print($"  [{Role}] {text}");
        Net.UndoAsked += asker =>
        {
            GD.Print($"  [host] {asker} asks to undo: allowing");
            Net.AnswerUndo(true);
        };
        GetTree().CreateTimer(TimeLimit).Timeout += () => Finish("timed out");
        if (hosting)
        {
            Net.MyName = "Host";
            Net.HostGame(Port);
            Net.ChooseGame(GamePath);
            Net.Sit(3);
            Net.SetBot(2, true);
            Net.SetBot(4, true);
            Net.LobbyChanged += () =>
            {
                if (Net.CanStart() && Net.Game == null)
                {
                    Net.StartMatch();
                }
            };
        }
        else
        {
            Net.MyName = "Guest";
            Net.Join("127.0.0.1", Port);
        }
    }

    void OnMatchStarted()
    {
        Game game = Net.Game!;
        game.Start();
        Net.CatchUp();
        bot = new(game, hosting ? 7 : 8);
        waitingFor = -1;
        GD.Print($"  [{Role}] match started as seat {game.LocalSeat} with {game.Inputs.Count} inputs so far");
        if (game.LocalSeat != (hosting ? 3 : 1))
        {
            GD.Print($"  [{Role}] PROBLEM: not in the seat it took (or was given)");
        }
        int otherPerson = hosting ? 1 : 3;
        Zone otherHand = game.Zones[$"hand_{otherPerson}"];
        Card? otherCard = otherHand.IsEmpty ? null : otherHand.Cards[0];
        if (game.IsBot(game.LocalSeat) || game.IsBot(otherPerson))
        {
            GD.Print($"  [{Role}] PROBLEM: a person's seat is played by a bot");
        }
        if (otherCard != null && (game.ShowsFace(otherCard) || game.MayHandle(otherCard)))
        {
            GD.Print($"  [{Role}] PROBLEM: can see or pick up the other person's cards");
        }
    }

    public override async void _Process(double delta)
    {
        Game? game = Net.Game;
        if (game == null || !Net.TableReady || finished)
        {
            return;
        }
        if (game.Plays.Offered.Contains("Next hand") || game.Over)
        {
            if (backInLobby)
            {
                Finish("second hand over");
            }
            else if (hosting)
            {
                GD.Print("  [host] hand over: back to the lobby for another match");
                Net.ReturnToLobby();
            }
            return;
        }
        if (game.Inputs.Count < waitingFor)
        {
            return;
        }
        if (!hosting && !askedUndo && game.Inputs.Count >= UndoAt)
        {
            askedUndo = true;
            GD.Print($"  [guest] asking to undo at {game.Inputs.Count} inputs");
            Net.RequestUndo();
            waitingFor = game.Inputs.Count + 1;
            return;
        }
        if (!hosting && !rejoined && game.Inputs.Count >= RejoinAt)
        {
            rejoined = true;
            GD.Print($"  [guest] dropping out at {game.Inputs.Count} inputs, then rejoining");
            Net.Leave();
            await ToSignal(GetTree().CreateTimer(1.0), Timer.SignalName.Timeout);
            Net.Join("127.0.0.1", Port);
            return;
        }
        if (!hosting && !corrupted && game.Inputs.Count >= CorruptAt)
        {
            corrupted = true;
            GD.Print($"  [guest] scribbling on its own copy at {game.Inputs.Count} inputs");
            game.Variables["tricks"] = (long)Values.ToDouble(game.Variables["tricks"]) + 5;
        }
        if (hosting && game.Plays.CanPerform("Collect trick"))
        {
            Net.Submit(PlayerInput.Perform("Collect trick"));
        }
        else if (hosting && game.IsBotTurn())
        {
            bot!.TakeInput();
        }
        else if (game.IsLocalTurn())
        {
            List<List<PlayerInput>> moves = game.Plays.LegalMoves();
            if (moves.Count == 0)
            {
                return;
            }
            int appliedBefore = game.Inputs.Count;
            foreach (PlayerInput input in moves[chooser.NextInRange(0, moves.Count - 1)])
            {
                Net.Submit(input);
            }
            if (!hosting)
            {
                waitingFor = appliedBefore + 1;
            }
        }
    }

    async void Finish(string reason)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        Game? game = Net.Game;
        GD.Print($"{Role.ToUpperInvariant()}: {reason} after {game?.Inputs.Count ?? 0} inputs, state {(game != null ? Snapshot.StateHash(game) : 0)}");
        if (game != null && reason == "timed out")
        {
            GD.Print($"  stuck: offered {string.Join(", ", game.Plays.Offered)}, player {game.CurrentPlayer()}, choosing '{game.Choice.Title}', local {game.IsLocalTurn()}, waiting_for {waitingFor}");
        }
        await ToSignal(GetTree().CreateTimer(hosting ? 3.0 : 0.5), Timer.SignalName.Timeout);
        GetTree().Quit();
    }
}
