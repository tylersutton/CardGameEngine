using Core;
using Godot;

/// The project's files (games, decks, templates, docs) by their res:// paths, wherever the project is on disk.
public static class ProjectFiles
{
    /// With text or bytes given in place of some files (the editor's unsaved edits, or an online host's game and images).
    public static GameFiles With(Dictionary<string, string>? given = null, Dictionary<string, byte[]>? givenBytes = null) =>
        new(ProjectSettings.GlobalizePath("res://"), given, givenBytes);
}
