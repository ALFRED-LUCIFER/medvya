namespace Medvya.Tenancy.Application;

/// <summary>The tenant has sent <see cref="Domain.InvitationRules.MaxPerHour"/> invitations in the last hour (#886); answered with 429.</summary>
public sealed class InvitationLimitException() : InvalidOperationException("Too many invitations in the last hour.");
