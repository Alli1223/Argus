namespace Argus.Server.Features.Auth;

public static class Roles
{
    /// <summary>Manages users and sees every host.</summary>
    public const string Admin = "Admin";

    /// <summary>Sees and manages only their own hosts.</summary>
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = [Admin, User];

    internal static readonly Guid AdminId = new("0b1c1d6e-5f0a-4c55-9d1e-2f1a3f6d0a01");
    internal static readonly Guid UserId = new("0b1c1d6e-5f0a-4c55-9d1e-2f1a3f6d0a02");
}
