namespace Em.Editing;

/// <summary>Describes one command of the sheet and writer editors, shared by the compact toolbar and the ribbon.</summary>
/// <param name="Id">The command identifier.</param>
/// <param name="Label">The caption shown on buttons and menus.</param>
/// <param name="IconKey">The key of the command icon.</param>
/// <param name="Group">The toolbar or ribbon group the command belongs to.</param>
/// <param name="RibbonTab">The ribbon tab that shows the command.</param>
/// <param name="Shortcut">The optional keyboard shortcut text.</param>
/// <param name="MutatesDocument">Whether the command changes the document, which hides it in viewer mode.</param>
public sealed record EditorCommand(string Id, string Label, string IconKey, string Group,
    string RibbonTab, string? Shortcut = null, bool MutatesDocument = false)
{
    /// <summary>The command whose drop-down lists this command. Toolbars show it only in that menu.</summary>
    public string? Parent { get; init; }

    /// <summary>The command only opens the drop-down of the commands that name it as <see cref="Parent"/>.</summary>
    public bool MenuOnly { get; init; }

    /// <summary>Checks whether the command can be used in an editor mode.</summary>
    /// <param name="mode">The editor mode.</param>
    /// <returns>True in full mode, and in viewer mode for commands that do not change the document.</returns>
    public bool IsAvailable(EditorMode mode) => mode == EditorMode.Full || !MutatesDocument;
}

/// <summary>The outcome of running an editor command.</summary>
/// <param name="Accepted">Whether the command ran.</param>
/// <param name="Reason">The message explaining why the command was rejected.</param>
public readonly record struct CommandResult(bool Accepted, string? Reason = null)
{
    /// <summary>Gets a result for a command that ran.</summary>
    public static CommandResult Success => new(true);
    /// <summary>Creates a result for a rejected command.</summary>
    /// <param name="reason">The message to show.</param>
    /// <returns>The result.</returns>
    public static CommandResult Rejected(string reason) => new(false, reason);
}
