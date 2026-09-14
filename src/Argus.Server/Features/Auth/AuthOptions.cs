namespace Argus.Server.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Argus:Auth";

    /// <summary>Let visitors create their own (non-admin) accounts from the sign-in page.</summary>
    public bool AllowRegistration { get; set; }
}
