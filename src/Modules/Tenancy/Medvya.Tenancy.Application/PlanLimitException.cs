namespace Medvya.Tenancy.Application;

/// <summary>The organisation's plan has no free doctor or staff seat (MDV-P1-069); answered with 409 and code plan_limit.</summary>
public sealed class PlanLimitException() : InvalidOperationException(Text)
{
    public const string Text = "Your plan allows 1 doctor and 2 staff; contact Medvya to upgrade";
}
