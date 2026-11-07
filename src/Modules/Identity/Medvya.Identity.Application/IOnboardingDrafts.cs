using Medvya.Identity.Domain;

namespace Medvya.Identity.Application;

/// <summary>The signed-in person's sign-up draft (MDV-P1-004). Every call is for the one user it names; drafts are never listed.</summary>
public interface IOnboardingDrafts
{
    /// <summary>The unfinished draft, or null when there is none (never started, or finished).</summary>
    Task<OnboardingDraft?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The user's unfinished draft, or a new one for <paramref name="country"/>. An unfinished draft keeps the country it started with.</summary>
    Task<OnboardingDraft> StartAsync(Guid userId, string country, CancellationToken cancellationToken = default);

    /// <summary>Replaces the draft's data and resume step. False when there is no unfinished draft.</summary>
    Task<bool> SaveAsync(Guid userId, int resumeStep, string data, CancellationToken cancellationToken = default);

    /// <summary>Marks the draft finished and replaces its data with <paramref name="result"/> (ids only). False when there is no unfinished draft.</summary>
    Task<bool> CompleteAsync(Guid userId, string result, CancellationToken cancellationToken = default);

    /// <summary>The result stored by <see cref="CompleteAsync"/> for the user's finished draft, or null when it is unfinished or there is none.</summary>
    Task<string?> CompletedResultAsync(Guid userId, CancellationToken cancellationToken = default);
}
