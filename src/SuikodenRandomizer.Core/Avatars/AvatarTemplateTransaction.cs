using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core.Avatars;

public enum AvatarTemplateTransactionOutcome
{
    Committed,
    PreflightRejected,
    RolledBack
}

/// <summary>
/// A copy-attempt record, not a native save. Detached snapshots are retained
/// only during Execute so the result does not keep native objects alive.
/// </summary>
public sealed class AvatarTemplateTransactionRecord
{
    internal AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome outcome,
        string? failureStage, Exception? failure, bool rollbackVerified)
    {
        Outcome = outcome;
        FailureStage = failureStage;
        Failure = failure;
        RollbackVerified = rollbackVerified;
    }

    public AvatarTemplateTransactionOutcome Outcome { get; }
    public bool Committed => Outcome == AvatarTemplateTransactionOutcome.Committed;
    public bool MutationsStarted => Outcome != AvatarTemplateTransactionOutcome.PreflightRejected;
    public string? FailureStage { get; }
    public Exception? Failure { get; }
    public bool RollbackVerified { get; }
}

/// <summary>
/// Rollback failure must reach the runtime's failure/save-blocking policy. It
/// must never be downgraded into an ordinary rejected template application.
/// </summary>
public sealed class AvatarTemplateRollbackException : Exception
{
    internal AvatarTemplateRollbackException(string stage, Exception originalFailure,
        IReadOnlyList<Exception> rollbackFailures)
        : base("Avatar template rollback could not restore/verify both role and donor snapshots.",
            new AggregateException(Combine(originalFailure, rollbackFailures)))
    {
        FailureStage = stage;
        OriginalFailure = originalFailure;
    }

    public string FailureStage { get; }
    public Exception OriginalFailure { get; }

    private static IEnumerable<Exception> Combine(Exception original, IReadOnlyList<Exception> rollbackFailures)
    {
        yield return original;
        foreach (Exception failure in rollbackFailures) yield return failure;
    }
}

/// <summary>
/// Production copy transaction orchestration. The native adapter must capture
/// detached snapshots, clone all donor-owned mutable templates, and implement
/// verifyRole for canonical IDs/plot fields and the chosen copy phase: intentional
/// donor starting progression at New Game, saved role progression on replay.
/// This utility detects donor mutation and verifies rollback; it does not guess
/// native template layouts or attempt to serialize native pointers.
/// </summary>
public static class AvatarTemplateTransaction
{
    public static AvatarTemplateTransactionRecord Execute<TTarget, TDonor>(
        Func<TTarget> captureRole,
        Func<TDonor> captureDonor,
        Action applyTemplate,
        Func<TTarget, TTarget, bool> verifyRole,
        Action<TTarget> restoreRole,
        Action<TDonor> restoreDonor,
        Func<TTarget, TTarget, bool> roleEqual,
        Func<TDonor, TDonor, bool> donorEqual)
    {
        if (captureRole is null) throw new ArgumentNullException(nameof(captureRole));
        if (captureDonor is null) throw new ArgumentNullException(nameof(captureDonor));
        if (applyTemplate is null) throw new ArgumentNullException(nameof(applyTemplate));
        if (verifyRole is null) throw new ArgumentNullException(nameof(verifyRole));
        if (restoreRole is null) throw new ArgumentNullException(nameof(restoreRole));
        if (restoreDonor is null) throw new ArgumentNullException(nameof(restoreDonor));
        if (roleEqual is null) throw new ArgumentNullException(nameof(roleEqual));
        if (donorEqual is null) throw new ArgumentNullException(nameof(donorEqual));

        string stage = "capture-role";
        TTarget roleBefore;
        TDonor donorBefore;
        try
        {
            roleBefore = captureRole();
            stage = "capture-donor";
            donorBefore = captureDonor();
        }
        catch (Exception exception)
        {
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.PreflightRejected,
                stage, exception, rollbackVerified: false);
        }

        try
        {
            stage = "apply-template";
            applyTemplate();
            stage = "verify-role";
            if (!verifyRole(roleBefore, captureRole()))
                throw new InvalidOperationException("Copied avatar violates canonical role/progression policy.");
            stage = "verify-donor";
            if (!donorEqual(donorBefore, captureDonor()))
                throw new InvalidOperationException("Copied avatar mutated the donor cache.");
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.Committed,
                failureStage: null, failure: null, rollbackVerified: false);
        }
        catch (Exception originalFailure)
        {
            var rollbackFailures = new List<Exception>();
            try { restoreRole(roleBefore); }
            catch (Exception exception) { rollbackFailures.Add(exception); }

            // Do not write to a healthy donor cache during ordinary rollback.
            // If comparison is unavailable, conservatively attempt restoration.
            bool donorNeedsRestore;
            try { donorNeedsRestore = !donorEqual(donorBefore, captureDonor()); }
            catch (Exception) { donorNeedsRestore = true; }
            if (donorNeedsRestore)
            {
                try { restoreDonor(donorBefore); }
                catch (Exception exception) { rollbackFailures.Add(exception); }
            }

            try
            {
                if (!roleEqual(roleBefore, captureRole()))
                    rollbackFailures.Add(new InvalidOperationException("Role rollback postcondition failed."));
            }
            catch (Exception exception) { rollbackFailures.Add(exception); }
            try
            {
                if (!donorEqual(donorBefore, captureDonor()))
                    rollbackFailures.Add(new InvalidOperationException("Donor rollback postcondition failed."));
            }
            catch (Exception exception) { rollbackFailures.Add(exception); }

            if (rollbackFailures.Count != 0)
                throw new AvatarTemplateRollbackException(stage, originalFailure, rollbackFailures);
            return new AvatarTemplateTransactionRecord(AvatarTemplateTransactionOutcome.RolledBack,
                stage, originalFailure, rollbackVerified: true);
        }
    }
}
