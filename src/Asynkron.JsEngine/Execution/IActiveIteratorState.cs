#region

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Common interface for state objects that hold an active iterator that needs to be closed
/// when a generator returns or throws. This unifies ArrayPatternState (for destructuring)
/// and IteratorDriverState (for for-of loops).
/// </summary>
internal interface IActiveIteratorState
{
    /// <summary>
    /// Attempts to get the active iterator for closing.
    /// Returns false if the iterator is already closed/done.
    /// </summary>
    bool TryGetActiveIterator(out IJsObjectLike iterator);

    /// <summary>
    /// Marks the iterator as closed so future calls to TryGetActiveIterator return false.
    /// </summary>
    void MarkIteratorClosed();
}
