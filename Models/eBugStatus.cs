namespace Flatline.Models
{
    public enum eBugStatus
    {
        Open = 0,
        InProgress = 1,
        Resolved = 2,
        Closed = 3,
        WontFix = 4,
        // AsDesigned = terminal. Use when the reported behavior is actually
        // intentional and the bug was a misunderstanding of how a feature is
        // supposed to work. A hint to the implementer that the code should
        // probably carry an inline marker explaining intent so the same
        // misread doesn't get re-filed later.
        AsDesigned = 5,
        // NeedsReview = active triage state. Use when an unverified issue is
        // found and needs investigation before it can move to Open or a
        // terminal state.
        NeedsReview = 6
    }
}
