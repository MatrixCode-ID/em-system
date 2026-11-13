namespace Em.Editing;

/// <summary>Whether a sheet or writer editor allows changes.</summary>
public enum EditorMode
{
    /// <summary>The document can be viewed and edited.</summary>
    Full,
    /// <summary>The document is read-only; commands that change it are hidden.</summary>
    Viewer
}
