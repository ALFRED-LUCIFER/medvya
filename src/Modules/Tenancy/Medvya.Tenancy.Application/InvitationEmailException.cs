namespace Medvya.Tenancy.Application;

/// <summary>The invitation email could not be handed to the mail service, so nothing was saved. An InvalidOperationException so callers that already count failed sends keep working.</summary>
public sealed class InvitationEmailException(Exception inner) : InvalidOperationException("The invitation email could not be sent.", inner);
