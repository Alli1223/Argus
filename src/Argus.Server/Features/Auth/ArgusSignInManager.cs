using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Auth;

/// <summary>Adds the "disabled by an administrator" check to Identity's sign-in rules.</summary>
public sealed class ArgusSignInManager(
    UserManager<ArgusUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ArgusUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ArgusUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ArgusUser> confirmation)
    : SignInManager<ArgusUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ArgusUser user) =>
        user.DisabledAt is null && await base.CanSignInAsync(user);
}
