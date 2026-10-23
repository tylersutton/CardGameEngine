using Core;
using Godot;

/// One seat in the lobby: who's in it (a peer, or a bot), and the name of whoever sat there, which stays after a
/// disconnect so they can rejoin.
public class Seat
{
    public long Peer { get; set; }
    public bool Bot { get; set; }
    public string Name { get; set; } = "";
}

/// The game the host chose, the numbers of players it allows, and its seats (one per player).
public record Lobby(string Game, string Title, string Description, bool CanBot, List<int> PlayerCounts, List<Seat> Seats);

/// Online play (an autoload, so it lasts across scenes). In the lobby, people take seats and the host fills the rest
/// with bots. In a match, every machine runs its own Game from the same rules, decks and seed; the host checks each
/// input with Game.Apply, numbers it and sends it to everyone, so all copies stay identical. Bots run only on the
/// host. Each input comes with the host's state hash, so a copy that drifts is rebuilt.
public partial class Net : Node
{
    public const int DefaultPort = 7777;
    const int MaxPeers = 8;
    const long Host = 1;
    const int NoSeat = 0;

    /// The autoload, for scenes to reach.
    public static Net Instance { get; private set; } = null!;

    public event Action? LobbyChanged;
    public event Action? MatchStarted;
    public event Action<string>? Notice;
    public event Action<string>? UndoAsked;
    public event Action? Disconnected;

    public bool Online { get; private set; }
    public string MyName { get; set; } = "Player";
    public Lobby? Lobby { get; private set; }
    public Game? Game { get; private set; }

    Dictionary<long, string> names = [];
    Godot.Collections.Dictionary matchPackage = [];
    List<(Godot.Collections.Dictionary PlayerInput, long? HostHash)> waitingInputs = [];
    /// The table is showing the match, so inputs from the host are applied as they come (see CatchUp).
    public bool TableReady { get; private set; }
    bool catchingUp;
    UndoRequest? undoRequest;

    /// An undo asked for by the player in a seat, waiting for the other people at the table to agree.
    record UndoRequest(int Seat, long Peer, List<long> Waiting);

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += () => RpcId(Host, MethodName.Hello, MyName);
        Multiplayer.ConnectionFailed += () => LoseConnection("Couldn't reach the host");
        Multiplayer.ServerDisconnected += () => LoseConnection("The host left");
    }

    public bool IsHost() => Online && Multiplayer.IsServer();

    public Error HostGame(int port)
    {
        ENetMultiplayerPeer peer = new();
        Error error = peer.CreateServer(port, MaxPeers);
        if (error != Error.Ok)
        {
            Notice?.Invoke($"Couldn't host on port {port}: {error}");
            return error;
        }
        GoOnline(peer);
        names = new() { [Host] = MyName };
        Notice?.Invoke($"Hosting on port {port}. Others join with this computer's address.");
        return Error.Ok;
    }

    public Error Join(string address, int port)
    {
        ENetMultiplayerPeer peer = new();
        Error error = peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            Notice?.Invoke($"Couldn't connect: {error}");
            return error;
        }
        GoOnline(peer);
        Notice?.Invoke($"Connecting to {address}:{port}...");
        return Error.Ok;
    }

    public void Leave()
    {
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = null;
        Online = false;
        names = [];
        Lobby = null;
        Game = null;
        TableReady = false;
        LobbyChanged?.Invoke();
    }

    void GoOnline(ENetMultiplayerPeer peer)
    {
        Multiplayer.MultiplayerPeer = peer;
        Online = true;
        Lobby = null;
    }

    void LoseConnection(string message)
    {
        Leave();
        Notice?.Invoke(message);
        Disconnected?.Invoke();
    }

    // Lobby

    public void ChooseGame(string path)
    {
        GameRules rules = GameRules.Load(ProjectFiles.With(), path);
        List<Seat> seats = Enumerable.Range(0, rules.Players).Select(_ => new Seat()).ToList();
        Lobby = new(path, rules.Title, rules.Description, rules.Bot.ContainsKey("prefer"), rules.PlayerCounts, seats);
        ShareLobby();
    }

    /// For a game that allows different numbers of players: seats are added or taken away at the end.
    public void ChoosePlayers(int count)
    {
        if (!IsHost() || Game != null || Lobby == null || !Lobby.PlayerCounts.Contains(count))
        {
            return;
        }
        while (Lobby.Seats.Count < count)
        {
            Lobby.Seats.Add(new());
        }
        Lobby.Seats.RemoveRange(count, Lobby.Seats.Count - count);
        ShareLobby();
    }

    public int MySeat()
    {
        int index = Lobby?.Seats.FindIndex(seat => seat.Peer == Multiplayer.GetUniqueId()) ?? -1;
        return index + 1;
    }

    public void Sit(int seat)
    {
        if (IsHost())
        {
            SitAs(Host, seat);
        }
        else
        {
            RpcId(Host, MethodName.RequestSeat, seat);
        }
    }

    public void SetBot(int seat, bool bot)
    {
        if (IsHost() && Lobby!.CanBot)
        {
            Lobby.Seats[seat - 1] = new() { Bot = bot };
            ShareLobby();
        }
    }

    public bool CanStart() => IsHost() && Lobby != null && Lobby.Seats.All(seat => seat.Bot || seat.Peer != 0);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hello(string playerName)
    {
        long peer = Multiplayer.GetRemoteSenderId();
        while (names.ContainsValue(playerName))
        {
            playerName += "+";
        }
        names[peer] = playerName;
        if (Game != null)
        {
            TakeBackSeat(peer, playerName);
        }
        ShareLobby();
        if (Game != null)
        {
            RpcId(peer, MethodName.BeginMatch, matchPackage, InputsAsVariants(Game.Inputs));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestSeat(int seat)
    {
        SitAs(Multiplayer.GetRemoteSenderId(), seat);
    }

    void SitAs(long peer, int seat)
    {
        if (Game != null || seat < 1 || seat > Lobby!.Seats.Count || Lobby.Seats[seat - 1].Peer != 0 || Lobby.Seats[seat - 1].Bot)
        {
            return;
        }
        foreach (Seat each in Lobby.Seats.Where(each => each.Peer == peer))
        {
            each.Peer = 0;
            each.Name = "";
        }
        Lobby.Seats[seat - 1] = new() { Peer = peer, Name = names[peer] };
        ShareLobby();
    }

    void ShareLobby()
    {
        Godot.Collections.Dictionary sentNames = [];
        foreach ((long peer, string name) in names)
        {
            sentNames[peer] = name;
        }
        Rpc(MethodName.ReceiveLobby, LobbyAsVariant(), sentNames);
    }

    [Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ReceiveLobby(Godot.Collections.Dictionary newLobby, Godot.Collections.Dictionary newNames)
    {
        Lobby = LobbyFrom(newLobby);
        names = newNames.ToDictionary(pair => pair.Key.AsInt64(), pair => pair.Value.AsString());
        if (Game != null)
        {
            Game.Rules.PlayerNames = SeatNames();
            Game.AnnounceStateChanged();
        }
        LobbyChanged?.Invoke();
    }

    void OnPeerDisconnected(long peer)
    {
        if (!IsHost())
        {
            return;
        }
        Notice?.Invoke($"{names.GetValueOrDefault(peer, "Someone")} left");
        names.Remove(peer);
        foreach (Seat seat in Lobby?.Seats.Where(seat => seat.Peer == peer && Game == null) ?? [])
        {
            seat.Peer = 0;
            seat.Name = "";
        }
        if (undoRequest != null && undoRequest.Waiting.Contains(peer))
        {
            UndoAnswered(peer, false);
        }
        ShareLobby();
    }

    /// Seat tags show each person's name, with "(away)" while they're disconnected. Bots are "Player 2" and so on:
    /// the game's own names (You, West...) describe one fixed point of view, and online each screen has its own.
    List<object?> SeatNames() =>
        Lobby!.Seats.Select((seat, i) => (object?)(seat.Bot ? $"Player {i + 1}" : seat.Name + (names.ContainsKey(seat.Peer) ? "" : " (away)"))).ToList();

    // Match

    public void StartMatch()
    {
        if (!CanStart())
        {
            return;
        }
        Godot.Collections.Array bots = [];
        for (int i = 0; i < Lobby!.Seats.Count; i++)
        {
            if (Lobby.Seats[i].Bot)
            {
                bots.Add(i + 1);
            }
        }
        Godot.Collections.Dictionary package = new()
        {
            ["path"] = Lobby.Game, ["files"] = MatchFiles(Lobby.Game), ["images"] = MatchImages(Lobby.Game), ["seed"] = (long)GD.Randi(), ["bots"] = bots,
            ["players"] = Lobby.Seats.Count,
        };
        Rpc(MethodName.BeginMatch, package, new Godot.Collections.Array());
    }

    static Godot.Collections.Dictionary MatchFiles(string path)
    {
        GameFiles files = ProjectFiles.With();
        Godot.Collections.Dictionary sent = new() { [path] = files.Read(path) };
        foreach (string deck in GameRules.Load(files, path).FilesUsed(path).Skip(1).Where(files.Exists))
        {
            sent[deck] = files.Read(deck);
        }
        return sent;
    }

    /// The game's images, sent with it so every player sees the same pictures without having them already.
    static Godot.Collections.Dictionary MatchImages(string path)
    {
        GameFiles files = ProjectFiles.With();
        Godot.Collections.Dictionary sent = [];
        foreach (string image in new Game(files, path).ImagePaths().Where(files.Exists))
        {
            sent[image] = files.ReadBytes(image);
        }
        return sent;
    }

    /// Builds this machine's copy of the match. The table calls CatchUp() once it's showing it.
    [Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void BeginMatch(Godot.Collections.Dictionary package, Godot.Collections.Array inputsSoFar)
    {
        Game? newGame = BuildMatch(package);
        if (newGame == null)
        {
            return;
        }
        matchPackage = package;
        Game = newGame;
        waitingInputs = inputsSoFar.Select(input => (input.AsGodotDictionary(), (long?)null)).ToList();
        TableReady = false;
        catchingUp = false;
        MatchStarted?.Invoke();
    }

    /// This machine's copy of the match, before any inputs: null (with a notice) if the game doesn't load.
    Game? BuildMatch(Godot.Collections.Dictionary package)
    {
        Dictionary<string, string> files = package["files"].AsGodotDictionary().ToDictionary(pair => pair.Key.AsString(), pair => pair.Value.AsString());
        Dictionary<string, byte[]> images = package["images"].AsGodotDictionary().ToDictionary(pair => pair.Key.AsString(), pair => pair.Value.AsByteArray());
        Game newGame = new(ProjectFiles.With(files, images), package["path"].AsString(), package["seed"].AsInt64(), package["players"].AsInt32());
        if (newGame.Errors.Count > 0)
        {
            Notice?.Invoke("Couldn't load the host's game: " + string.Join("; ", newGame.Errors));
            return null;
        }
        newGame.Rules.Bot["players"] = package["bots"].AsGodotArray().Select(bot => (object?)(double)bot.AsInt32()).ToList();
        newGame.Rules.PlayerNames = SeatNames();
        int seat = MySeat();
        newGame.LocalSeat = seat != NoSeat ? seat : newGame.Rules.Players + 1;
        return newGame;
    }

    /// The host only starts sending its inputs once it has caught up, so a rebuilt match isn't sent twice.
    public void CatchUp()
    {
        TableReady = true;
        foreach ((Godot.Collections.Dictionary input, long? hostHash) in waitingInputs)
        {
            ApplyFromHost(input, hostHash);
        }
        waitingInputs.Clear();
        if (IsHost())
        {
            Game!.InputApplied += SendInput;
        }
    }

    /// Someone joining a match in progress gets back the seat they left (same name), or watches.
    void TakeBackSeat(long peer, string playerName)
    {
        Seat? seat = Lobby!.Seats.FirstOrDefault(seat => !seat.Bot && seat.Name == playerName && !names.ContainsKey(seat.Peer));
        if (seat != null)
        {
            seat.Peer = peer;
            Notice?.Invoke(playerName + " rejoined");
        }
    }

    /// The table's way in: the host applies its own inputs as its seat; everyone else asks the host.
    /// A drag's answer is a guess (is it allowed here?) so the card doesn't snap back while the host answers.
    public bool Submit(PlayerInput input)
    {
        if (input.Type == "undo")
        {
            RequestUndo();
            return false;
        }
        if (IsHost())
        {
            return Game!.Apply(input with { Player = Game.LocalSeat });
        }
        RpcId(Host, MethodName.RequestInput, GodotValues.ToDictionary(input.ToPlain()));
        bool mayAct = Game!.MayAct(Game.LocalSeat, input);
        return mayAct && (input.Type != "drag" || Game.Plays.CanMove(Game.Cards[input.Card!.Value], Game.Zones[input.To!]));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestInput(Godot.Collections.Dictionary input)
    {
        int seat = SeatOf(Multiplayer.GetRemoteSenderId());
        if (Game == null || seat == NoSeat)
        {
            return;
        }
        Game.Apply(InputFrom(input) with { Player = seat });
    }

    void SendInput(PlayerInput input)
    {
        Rpc(MethodName.ReceiveInput, GodotValues.ToDictionary(input.ToPlain()), Snapshot.StateHash(Game!));
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ReceiveInput(Godot.Collections.Dictionary input, long hostHash)
    {
        if (catchingUp)
        {
            return;
        }
        if (TableReady)
        {
            ApplyFromHost(input, hostHash);
        }
        else
        {
            waitingInputs.Add((input, hostHash));
        }
    }

    void ApplyFromHost(Godot.Collections.Dictionary input, long? hostHash)
    {
        Game!.Apply(InputFrom(input));
        if (hostHash != null && Snapshot.StateHash(Game) != hostHash)
        {
            Notice?.Invoke("Out of step with the host; catching up");
            catchingUp = true;
            RpcId(Host, MethodName.RequestResync);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    /// A copy that drifted is rebuilt from the host's inputs. The host first checks its own copy against those inputs
    /// replayed: if even that differs, every copy (the host's too) is rebuilt from them, so they all agree again.
    void RequestResync()
    {
        Godot.Collections.Array inputs = InputsAsVariants(Game!.Inputs);
        Game? replayed = BuildMatch(matchPackage);
        if (replayed == null)
        {
            return;
        }
        replayed.Start();
        foreach (PlayerInput input in Game.Inputs)
        {
            replayed.Apply(input);
        }
        if (Snapshot.StateHash(replayed) != Snapshot.StateHash(Game))
        {
            Notice?.Invoke("The host's copy was out of step with its own moves; rebuilding the match");
            Rpc(MethodName.BeginMatch, matchPackage, inputs);
            return;
        }
        RpcId(Multiplayer.GetRemoteSenderId(), MethodName.BeginMatch, matchPackage, inputs);
    }

    int SeatOf(long peer) => (Lobby?.Seats.FindIndex(seat => seat.Peer == peer) ?? -1) + 1;

    // Undo: everyone else at the table has to agree

    public void RequestUndo()
    {
        if (IsHost())
        {
            UndoRequested(Host);
        }
        else
        {
            RpcId(Host, MethodName.AskHostToUndo);
        }
    }

    public void AnswerUndo(bool allow)
    {
        if (IsHost())
        {
            UndoAnswered(Host, allow);
        }
        else
        {
            RpcId(Host, MethodName.AnswerHostUndo, allow);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void AskHostToUndo()
    {
        UndoRequested(Multiplayer.GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void AnswerHostUndo(bool allow)
    {
        UndoAnswered(Multiplayer.GetRemoteSenderId(), allow);
    }

    void UndoRequested(long peer)
    {
        int seat = SeatOf(peer);
        if (seat == NoSeat || undoRequest != null)
        {
            return;
        }
        List<long> others = Lobby!.Seats.Select(each => each.Peer).Where(other => other != 0 && other != peer && names.ContainsKey(other)).ToList();
        if (others.Count == 0)
        {
            Game!.Apply(new("undo") { Player = seat });
            return;
        }
        // Its own list: the host may answer at once, while the others are still being asked.
        undoRequest = new(seat, peer, [.. others]);
        foreach (long other in others)
        {
            if (other == Host)
            {
                UndoAsked?.Invoke(names[peer]);
            }
            else
            {
                RpcId(other, MethodName.AskUndo, names[peer]);
            }
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void AskUndo(string asker)
    {
        UndoAsked?.Invoke(asker);
    }

    void UndoAnswered(long peer, bool allow)
    {
        if (undoRequest == null || !undoRequest.Waiting.Contains(peer))
        {
            return;
        }
        if (!allow)
        {
            Tell(undoRequest.Peer, $"{names.GetValueOrDefault(peer, "Someone")} didn't allow the undo");
            undoRequest = null;
            return;
        }
        undoRequest.Waiting.Remove(peer);
        if (undoRequest.Waiting.Count == 0)
        {
            Game!.Apply(new("undo") { Player = undoRequest.Seat });
            undoRequest = null;
        }
    }

    void Tell(long peer, string text)
    {
        if (peer == Host)
        {
            Notice?.Invoke(text);
        }
        else if (names.ContainsKey(peer))
        {
            RpcId(peer, MethodName.ReceiveNotice, text);
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ReceiveNotice(string text)
    {
        Notice?.Invoke(text);
    }

    static PlayerInput InputFrom(Godot.Collections.Dictionary input) => PlayerInput.FromPlain(GodotValues.FromDictionary(input));

    static Godot.Collections.Array InputsAsVariants(List<PlayerInput> inputs) => new(inputs.Select(input => (Variant)GodotValues.ToDictionary(input.ToPlain())));

    Godot.Collections.Dictionary LobbyAsVariant()
    {
        if (Lobby == null)
        {
            return [];
        }
        Godot.Collections.Array seats = new(Lobby.Seats.Select(seat => (Variant)new Godot.Collections.Dictionary
        {
            ["peer"] = seat.Peer, ["bot"] = seat.Bot, ["name"] = seat.Name,
        }));
        return new()
        {
            ["game"] = Lobby.Game, ["title"] = Lobby.Title, ["description"] = Lobby.Description, ["can_bot"] = Lobby.CanBot,
            ["player_counts"] = new Godot.Collections.Array(Lobby.PlayerCounts.Select(count => (Variant)count)), ["seats"] = seats,
        };
    }

    static Lobby? LobbyFrom(Godot.Collections.Dictionary sent)
    {
        if (!sent.ContainsKey("seats"))
        {
            return null;
        }
        List<Seat> seats = sent["seats"].AsGodotArray().Select(each =>
        {
            Godot.Collections.Dictionary seat = each.AsGodotDictionary();
            return new Seat { Peer = seat["peer"].AsInt64(), Bot = seat["bot"].AsBool(), Name = seat["name"].AsString() };
        }).ToList();
        List<int> counts = sent["player_counts"].AsGodotArray().Select(count => count.AsInt32()).ToList();
        return new(sent["game"].AsString(), sent["title"].AsString(), sent["description"].AsString(), sent["can_bot"].AsBool(), counts, seats);
    }
}
