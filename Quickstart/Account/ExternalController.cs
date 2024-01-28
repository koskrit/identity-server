using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4;
using IdentityServer4.Events;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using IdentityServerBackend.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IdentityServerHost.Quickstart.UI
{
    [SecurityHeaders]
    [AllowAnonymous]
    public class ExternalController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IIdentityServerInteractionService _interaction;
        private readonly IClientStore _clientStore;
        private readonly IEventService _events;
        private readonly ILogger<ExternalController> _logger;

        public ExternalController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IIdentityServerInteractionService interaction,
            IClientStore clientStore,
            IEventService events,
            ILogger<ExternalController> logger
        )
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _interaction = interaction;
            _clientStore = clientStore;
            _events = events;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Challenge(string scheme, string returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl))
                returnUrl = "~/";

            if (
                Url.IsLocalUrl(returnUrl) == false
                && _interaction.IsValidReturnUrl(returnUrl) == false
            )
            {
                throw new Exception("invalid return URL");
            }

            var props = new AuthenticationProperties
            {
                RedirectUri = Url.Action(nameof(Callback)),
                Items = { { "returnUrl", returnUrl }, { "scheme", scheme }, }
            };

            return Challenge(props, scheme);
        }

        [HttpGet]
        public async Task<IActionResult> Callback()
        {
            var result = await HttpContext.AuthenticateAsync(
                IdentityServerConstants.ExternalCookieAuthenticationScheme
            );
            if (result?.Succeeded != true)
            {
                throw new Exception("External authentication error");
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var externalClaims = result.Principal.Claims.Select(c => $"{c.Type}: {c.Value}");
                _logger.LogDebug("External claims: {@claims}", externalClaims);
            }

            var (user, provider, providerUserId, claims) = await FindUserFromExternalProviderAsync(
                result
            );
            if (user == null)
            {
                user = await AutoProvisionUserAsync(provider, providerUserId, claims);
            }

            var additionalLocalClaims = new List<Claim>();
            var localSignInProps = new AuthenticationProperties();
            ProcessLoginCallback(result, additionalLocalClaims, localSignInProps);

            var principal = await _signInManager.CreateUserPrincipalAsync(user);
            additionalLocalClaims.AddRange(principal.Claims);
            var name = principal.FindFirst(JwtClaimTypes.Name)?.Value ?? user.Id;

            var isuser = new IdentityServerUser(user.Id)
            {
                DisplayName = name,
                IdentityProvider = provider,
                AdditionalClaims = additionalLocalClaims
            };

            await HttpContext.SignInAsync(isuser, localSignInProps);

            await HttpContext.SignOutAsync(
                IdentityServerConstants.ExternalCookieAuthenticationScheme
            );

            var returnUrl = result.Properties.Items["returnUrl"] ?? "~/";

            var context = await _interaction.GetAuthorizationContextAsync(returnUrl);
            await _events.RaiseAsync(
                new UserLoginSuccessEvent(
                    provider,
                    providerUserId,
                    user.Id,
                    name,
                    true,
                    context?.Client.ClientId
                )
            );

            if (context != null)
            {
                if (context.IsNativeClient())
                {
                    return this.LoadingPage("Redirect", returnUrl);
                }
            }

            return Redirect(returnUrl);
        }

        private async Task<(
            ApplicationUser user,
            string provider,
            string providerUserId,
            IEnumerable<Claim> claims
        )> FindUserFromExternalProviderAsync(AuthenticateResult result)
        {
            var externalUser = result.Principal;

            var userIdClaim =
                externalUser.FindFirst(JwtClaimTypes.Subject)
                ?? externalUser.FindFirst(ClaimTypes.NameIdentifier)
                ?? throw new Exception("Unknown userid");

            var claims = externalUser.Claims.ToList();
            claims.Remove(userIdClaim);

            var provider = result.Properties.Items["scheme"];
            var providerUserId = userIdClaim.Value;

            var user = await _userManager.FindByLoginAsync(provider, providerUserId);

            return (user, provider, providerUserId, claims);
        }

        private async Task<ApplicationUser> AutoProvisionUserAsync(
            string provider,
            string providerUserId,
            IEnumerable<Claim> claims
        )
        {
            var filtered = new List<Claim>();

            var name =
                claims.FirstOrDefault(x => x.Type == JwtClaimTypes.Name)?.Value
                ?? claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
            if (name != null)
            {
                filtered.Add(new Claim(JwtClaimTypes.Name, name));
            }
            else
            {
                var first =
                    claims.FirstOrDefault(x => x.Type == JwtClaimTypes.GivenName)?.Value
                    ?? claims.FirstOrDefault(x => x.Type == ClaimTypes.GivenName)?.Value;
                var last =
                    claims.FirstOrDefault(x => x.Type == JwtClaimTypes.FamilyName)?.Value
                    ?? claims.FirstOrDefault(x => x.Type == ClaimTypes.Surname)?.Value;
                if (first != null && last != null)
                {
                    filtered.Add(new Claim(JwtClaimTypes.Name, first + " " + last));
                }
                else if (first != null)
                {
                    filtered.Add(new Claim(JwtClaimTypes.Name, first));
                }
                else if (last != null)
                {
                    filtered.Add(new Claim(JwtClaimTypes.Name, last));
                }
            }

            var email =
                claims.FirstOrDefault(x => x.Type == JwtClaimTypes.Email)?.Value
                ?? claims.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value;
            if (email != null)
            {
                filtered.Add(new Claim(JwtClaimTypes.Email, email));
            }

            var user = new ApplicationUser { UserName = Guid.NewGuid().ToString(), };
            var identityResult = await _userManager.CreateAsync(user);
            if (!identityResult.Succeeded)
                throw new Exception(identityResult.Errors.First().Description);

            if (filtered.Any())
            {
                identityResult = await _userManager.AddClaimsAsync(user, filtered);
                if (!identityResult.Succeeded)
                    throw new Exception(identityResult.Errors.First().Description);
            }

            identityResult = await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(provider, providerUserId, provider)
            );
            if (!identityResult.Succeeded)
                throw new Exception(identityResult.Errors.First().Description);

            return user;
        }

        private void ProcessLoginCallback(
            AuthenticateResult externalResult,
            List<Claim> localClaims,
            AuthenticationProperties localSignInProps
        )
        {
            var sid = externalResult.Principal.Claims.FirstOrDefault(x =>
                x.Type == JwtClaimTypes.SessionId
            );
            if (sid != null)
            {
                localClaims.Add(new Claim(JwtClaimTypes.SessionId, sid.Value));
            }

            var idToken = externalResult.Properties.GetTokenValue("id_token");
            if (idToken != null)
            {
                localSignInProps.StoreTokens(
                    new[]
                    {
                        new AuthenticationToken { Name = "id_token", Value = idToken }
                    }
                );
            }
        }
    }
}
