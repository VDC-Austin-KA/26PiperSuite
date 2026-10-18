using Autodesk.Revit.DB;

namespace PipingIsoSuite.Revit.Services;

public sealed record GuardResult(IReadOnlySet<long> Blocked, IReadOnlyList<string> Messages)
{
    public bool IsEditable(long id) => !Blocked.Contains(id);
}

/// <summary>
/// Checks workshared ownership/currency before batch edits and reserves (checks out) the rest,
/// so a long batch never fails half-way on an element another user owns.
/// Call before starting the transaction: checkout talks to central and is not allowed mid-transaction.
/// </summary>
public static class WorksharingGuard
{
    public static GuardResult Reserve(Document doc, IEnumerable<ElementId> ids)
    {
        var list = ids.Distinct().ToList();
        if (!doc.IsWorkshared || list.Count == 0) return new(new HashSet<long>(), Array.Empty<string>());

        var blocked = new HashSet<long>();
        var owners = new Dictionary<string, int>();
        var stale = 0;
        foreach (var id in list)
        {
            if (WorksharingUtils.GetCheckoutStatus(doc, id, out var owner) == CheckoutStatus.OwnedByOtherUser)
            {
                blocked.Add(id.Value);
                owners[owner] = owners.GetValueOrDefault(owner) + 1;
                continue;
            }
            var updates = WorksharingUtils.GetModelUpdatesStatus(doc, id);
            if (updates is ModelUpdatesStatus.UpdatedInCentral or ModelUpdatesStatus.DeletedInCentral)
            { blocked.Add(id.Value); stale++; }
        }

        var messages = owners.Select(kv => $"{kv.Value} element(s) owned by {kv.Key} were skipped.").ToList();
        if (stale > 0) messages.Add($"{stale} element(s) changed in central - Reload Latest, then re-run.");

        var wanted = list.Where(i => !blocked.Contains(i.Value)).ToList();
        try
        {
            var reserved = WorksharingUtils.CheckoutElements(doc, wanted).Select(i => i.Value).ToHashSet();
            var refused = wanted.Where(i => !reserved.Contains(i.Value)).ToList();
            foreach (var id in refused) blocked.Add(id.Value);
            if (refused.Count > 0) messages.Add($"{refused.Count} element(s) could not be checked out and were skipped.");
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            // Detached/offline: Revit borrows on commit instead; report and continue.
            messages.Add($"Could not reserve elements in central ({ex.Message}).");
        }
        return new(blocked, messages);
    }
}
