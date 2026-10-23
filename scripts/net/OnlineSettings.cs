using Godot;

/// What the online screens remember between launches, so nobody retypes them: your name, the address and ports, the
/// host's last game, and whether you last hosted or joined (for the title screen's Host again / Rejoin button).
public class OnlineSettings
{
    public const string Hosted = "host";
    public const string Joined = "join";
    const string FilePath = "user://online.cfg";
    const string Section = "online";

    public string Name { get; set; } = "Player";
    public string Address { get; set; } = "127.0.0.1";
    public int HostPort { get; set; } = Net.DefaultPort;
    public int JoinPort { get; set; } = Net.DefaultPort;
    public string Game { get; set; } = "";
    /// Hosted, Joined, or empty before the first time online.
    public string LastConnection { get; set; } = "";

    public static OnlineSettings Load()
    {
        OnlineSettings settings = new();
        ConfigFile file = new();
        if (file.Load(FilePath) != Error.Ok)
        {
            return settings;
        }
        settings.Name = file.GetValue(Section, "name", settings.Name).AsString();
        settings.Address = file.GetValue(Section, "address", settings.Address).AsString();
        settings.HostPort = file.GetValue(Section, "host_port", settings.HostPort).AsInt32();
        settings.JoinPort = file.GetValue(Section, "join_port", settings.JoinPort).AsInt32();
        settings.Game = file.GetValue(Section, "game", settings.Game).AsString();
        settings.LastConnection = file.GetValue(Section, "last_connection", settings.LastConnection).AsString();
        return settings;
    }

    public void Save()
    {
        ConfigFile file = new();
        file.SetValue(Section, "name", Name);
        file.SetValue(Section, "address", Address);
        file.SetValue(Section, "host_port", HostPort);
        file.SetValue(Section, "join_port", JoinPort);
        file.SetValue(Section, "game", Game);
        file.SetValue(Section, "last_connection", LastConnection);
        file.Save(FilePath);
    }
}
