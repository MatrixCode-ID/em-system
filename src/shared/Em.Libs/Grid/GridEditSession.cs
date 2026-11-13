using Em.Grid.Data;

namespace Em.Grid;

/// <summary>Lifecycle state of a <see cref="GridEditSession"/>.</summary>
public enum EditStatus
{
    /// <summary>The edit is open and can be committed or canceled.</summary>
    Active,
    /// <summary>A commit is in progress.</summary>
    Committing,
    /// <summary>The provider accepted the new value.</summary>
    Committed,
    /// <summary>The edit was abandoned.</summary>
    Canceled
}

/// <summary>Tracks one in-place cell edit from start to commit or cancel.</summary>
/// <param name="address">The edited cell.</param>
/// <param name="original">The value the cell had when the edit started.</param>
public sealed class GridEditSession(CellAddress address, GridValue original)
{
    /// <summary>Gets the edited cell.</summary>
    public CellAddress Address { get; } = address;
    /// <summary>Gets the value the cell had when the edit started; providers use it for optimistic concurrency checks.</summary>
    public GridValue Original { get; } = original;
    /// <summary>Gets the current lifecycle state.</summary>
    public EditStatus Status { get; private set; }

    /// <summary>Abandons the edit. Throws when the session is no longer active.</summary>
    public void Cancel()
    {
        EnsureActive();
        Status = EditStatus.Canceled;
    }

    /// <summary>Sends the new value to the provider. The session becomes committed when the provider reports an update and stays active otherwise, so the user can retry.</summary>
    /// <param name="provider">The data provider that stores the value.</param>
    /// <param name="value">The new cell value.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The provider's update result.</returns>
    public async ValueTask<CellUpdateResult> CommitAsync(
        IGridDataProvider provider, GridValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureActive();
        Status = EditStatus.Committing;
        try
        {
            var result = await provider.UpdateAsync(Address, Original, value, cancellationToken).ConfigureAwait(false);
            Status = result == CellUpdateResult.Updated ? EditStatus.Committed : EditStatus.Active;
            return result;
        }
        finally
        {
            if (Status == EditStatus.Committing)
                Status = EditStatus.Active;
        }
    }

    private void EnsureActive()
    {
        if (Status != EditStatus.Active)
            throw new InvalidOperationException("The edit session is no longer available.");
    }
}
