using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>All donors are captured before any role is changed; rollback covers the complete batch.</summary>
public static class AvatarShuffleTransaction
{
    public static AvatarTemplateTransactionRecord Execute<T>(IReadOnlyList<int> roleIds,
        Func<int, T> capture, Action<int, IReadOnlyDictionary<int, T>> apply,
        Func<int, T, T, bool> verifyRole, Action<int, T> restore,
        Func<T, T, bool> equal, Func<bool> verifySharedState)
    {
        if (roleIds is null) throw new ArgumentNullException(nameof(roleIds));
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (apply is null) throw new ArgumentNullException(nameof(apply));
        if (verifyRole is null) throw new ArgumentNullException(nameof(verifyRole));
        if (restore is null) throw new ArgumentNullException(nameof(restore));
        if (equal is null) throw new ArgumentNullException(nameof(equal));
        if (verifySharedState is null) throw new ArgumentNullException(nameof(verifySharedState));
        var ids = new List<int>(roleIds.Count);
        var distinct = new HashSet<int>();
        foreach (int id in roleIds)
        {
            if (!distinct.Add(id)) throw new ArgumentException("A shuffle batch cannot contain duplicate roles.", nameof(roleIds));
            ids.Add(id);
        }
        if (ids.Count == 0) throw new ArgumentException("A shuffle batch cannot be empty.", nameof(roleIds));
        var before = new Dictionary<int, T>();
        var snapshots = new ReadOnlyDictionary<int, T>(before);
        string stage = "capture-all-originals";
        try
        {
            foreach (int id in ids) before.Add(id, capture(id));
            stage = "verify-shared-preflight";
            if (!verifySharedState()) throw new InvalidOperationException("Shared canonical state failed preflight.");
        }
        catch (Exception exception)
        {
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.PreflightRejected, stage, exception, false);
        }
        try
        {
            foreach (int id in ids) { stage = "apply-role-" + id; apply(id, snapshots); }
            foreach (int id in ids)
            {
                stage = "verify-role-" + id;
                if (!verifyRole(id, before[id], capture(id)))
                    throw new InvalidOperationException("Avatar shuffle role failed identity/progression verification.");
            }
            stage = "verify-shared-state";
            if (!verifySharedState()) throw new InvalidOperationException("Avatar shuffle changed protected shared canonical state.");
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.Committed, null, null, false);
        }
        catch (Exception originalFailure)
        {
            var failures = new List<Exception>();
            // Restore every captured role, including not-yet-applied donors:
            // a defective native setter may have affected another record.
            for (int n = ids.Count - 1; n >= 0; n--)
            {
                int id = ids[n];
                try { restore(id, before[id]); }
                catch (Exception exception) { failures.Add(exception); }
            }
            foreach (int id in ids)
            {
                try
                {
                    if (!equal(before[id], capture(id)))
                        failures.Add(new InvalidOperationException("Avatar shuffle rollback mismatch for role " + id + "."));
                }
                catch (Exception exception) { failures.Add(exception); }
            }
            try
            {
                if (!verifySharedState()) failures.Add(new InvalidOperationException("Shared canonical state was not restored."));
            }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count != 0) throw new AvatarShuffleRollbackException(stage, originalFailure, failures);
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.RolledBack, stage, originalFailure, true);
        }
    }
}

public sealed class AvatarShuffleRollbackException : Exception
{
    internal AvatarShuffleRollbackException(string stage, Exception originalFailure, IReadOnlyList<Exception> failures)
        : base("Avatar shuffle rollback could not restore or verify the complete role batch.", new AggregateException(Combine(originalFailure, failures)))
    { FailureStage = stage; OriginalFailure = originalFailure; }
    public string FailureStage { get; }
    public Exception OriginalFailure { get; }
    private static IEnumerable<Exception> Combine(Exception original, IReadOnlyList<Exception> failures)
    { yield return original; foreach (Exception failure in failures) yield return failure; }
}
