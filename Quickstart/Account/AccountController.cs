﻿using System;
using System.Linq;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4.Events;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using IdentityServerBackend.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace IdentityServerHost.Quickstart.UI
{
    [SecurityHeaders]
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IIdentityServerInteractionService _interaction;
        private readonly IClientStore _clientStore;
        private readonly IAuthenticationSchemeProvider _schemeProvider;
        private readonly IEventService _events;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IIdentityServerInteractionService interaction,
            IClientStore clientStore,
            IAuthenticationSchemeProvider schemeProvider,
            IEventService events
        )
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _interaction = interaction;
            _clientStore = clientStore;
            _schemeProvider = schemeProvider;
            _events = events;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string returnUrl)
        {
            var vm = await BuildLoginViewModelAsync(returnUrl);

            if (vm.IsExternalLoginOnly)
            {
                return RedirectToAction(
                    "Challenge",
                    "External",
                    new { scheme = vm.ExternalLoginScheme, returnUrl }
                );
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginInputModel model, string button)
        {
            var context = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);

            if (button != "login")
            {
                if (context != null)
                {
                    await _interaction.DenyAuthorizationAsync(
                        context,
                        AuthorizationError.AccessDenied
                    );

                    if (context.IsNativeClient())
                    {
                        return this.LoadingPage("Redirect", model.ReturnUrl);
                    }

                    return Redirect(model.ReturnUrl);
                }
                else
                {
                    return Redirect("~/");
                }
            }

            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(
                    model.Username,
                    model.Password,
                    model.RememberLogin,
                    lockoutOnFailure: true
                );
                if (result.Succeeded)
                {
                    var user = await _userManager.FindByNameAsync(model.Username);
                    await _events.RaiseAsync(
                        new UserLoginSuccessEvent(
                            user.UserName,
                            user.Id,
                            user.UserName,
                            clientId: context?.Client.ClientId
                        )
                    );

                    if (context != null)
                    {
                        if (context.IsNativeClient())
                        {
                            return this.LoadingPage("Redirect", model.ReturnUrl);
                        }

                        return Redirect(model.ReturnUrl);
                    }

                    if (Url.IsLocalUrl(model.ReturnUrl))
                    {
                        return Redirect(model.ReturnUrl);
                    }
                    else if (string.IsNullOrEmpty(model.ReturnUrl))
                    {
                        return Redirect("~/");
                    }
                    else
                    {
                        throw new Exception("invalid return URL");
                    }
                }

                await _events.RaiseAsync(
                    new UserLoginFailureEvent(
                        model.Username,
                        "invalid credentials",
                        clientId: context?.Client.ClientId
                    )
                );
                ModelState.AddModelError(
                    string.Empty,
                    AccountOptions.InvalidCredentialsErrorMessage
                );
            }

            var vm = await BuildLoginViewModelAsync(model);
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Logout(string logoutId)
        {
            var vm = await BuildLogoutViewModelAsync(logoutId);

            if (vm.ShowLogoutPrompt == false)
            {
                return await Logout(vm);
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(LogoutInputModel model)
        {
            var vm = await BuildLoggedOutViewModelAsync(model.LogoutId);

            if (User?.Identity.IsAuthenticated == true)
            {
                await _signInManager.SignOutAsync();

                await _events.RaiseAsync(
                    new UserLogoutSuccessEvent(User.GetSubjectId(), User.GetDisplayName())
                );
            }

            if (vm.TriggerExternalSignout)
            {
                string url = Url.Action("Logout", new { logoutId = vm.LogoutId });

                return SignOut(
                    new AuthenticationProperties { RedirectUri = url },
                    vm.ExternalAuthenticationScheme
                );
            }

            if (
                vm.AutomaticRedirectAfterSignOut
                && !string.IsNullOrWhiteSpace(vm.PostLogoutRedirectUri)
            )
            {
                return Redirect(vm.PostLogoutRedirectUri);
            }
            else
            {
                return View("LoggedOut", vm);
            }
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        /*****************************************/
        /* helper APIs for the AccountController */
        /*****************************************/
        private async Task<LoginViewModel> BuildLoginViewModelAsync(string returnUrl)
        {
            var context = await _interaction.GetAuthorizationContextAsync(returnUrl);
            if (context?.IdP != null && await _schemeProvider.GetSchemeAsync(context.IdP) != null)
            {
                var local =
                    context.IdP == IdentityServer4.IdentityServerConstants.LocalIdentityProvider;

                var vm = new LoginViewModel
                {
                    EnableLocalLogin = local,
                    ReturnUrl = returnUrl,
                    Username = context?.LoginHint,
                };

                if (!local)
                {
                    vm.ExternalProviders = new[]
                    {
                        new ExternalProvider { AuthenticationScheme = context.IdP }
                    };
                }

                return vm;
            }

            var schemes = await _schemeProvider.GetAllSchemesAsync();

            var providers = schemes
                .Where(x => x.DisplayName != null)
                .Select(x => new ExternalProvider
                {
                    DisplayName = x.DisplayName ?? x.Name,
                    AuthenticationScheme = x.Name
                })
                .ToList();

            var allowLocal = true;
            if (context?.Client.ClientId != null)
            {
                var client = await _clientStore.FindEnabledClientByIdAsync(context.Client.ClientId);
                if (client != null)
                {
                    allowLocal = client.EnableLocalLogin;

                    if (
                        client.IdentityProviderRestrictions != null
                        && client.IdentityProviderRestrictions.Any()
                    )
                    {
                        providers = providers
                            .Where(provider =>
                                client.IdentityProviderRestrictions.Contains(
                                    provider.AuthenticationScheme
                                )
                            )
                            .ToList();
                    }
                }
            }

            return new LoginViewModel
            {
                AllowRememberLogin = AccountOptions.AllowRememberLogin,
                EnableLocalLogin = allowLocal && AccountOptions.AllowLocalLogin,
                ReturnUrl = returnUrl,
                Username = context?.LoginHint,
                ExternalProviders = providers.ToArray()
            };
        }

        private async Task<LoginViewModel> BuildLoginViewModelAsync(LoginInputModel model)
        {
            var vm = await BuildLoginViewModelAsync(model.ReturnUrl);
            vm.Username = model.Username;
            vm.RememberLogin = model.RememberLogin;
            return vm;
        }

        private async Task<LogoutViewModel> BuildLogoutViewModelAsync(string logoutId)
        {
            var vm = new LogoutViewModel
            {
                LogoutId = logoutId,
                ShowLogoutPrompt = AccountOptions.ShowLogoutPrompt
            };

            if (User?.Identity.IsAuthenticated != true)
            {
                vm.ShowLogoutPrompt = false;
                return vm;
            }

            var context = await _interaction.GetLogoutContextAsync(logoutId);
            if (context?.ShowSignoutPrompt == false)
            {
                vm.ShowLogoutPrompt = false;
                return vm;
            }

            return vm;
        }

        private async Task<LoggedOutViewModel> BuildLoggedOutViewModelAsync(string logoutId)
        {
            var logout = await _interaction.GetLogoutContextAsync(logoutId);

            var vm = new LoggedOutViewModel
            {
                AutomaticRedirectAfterSignOut = AccountOptions.AutomaticRedirectAfterSignOut,
                PostLogoutRedirectUri = logout?.PostLogoutRedirectUri,
                ClientName = string.IsNullOrEmpty(logout?.ClientName)
                    ? logout?.ClientId
                    : logout?.ClientName,
                SignOutIframeUrl = logout?.SignOutIFrameUrl,
                LogoutId = logoutId
            };

            if (User?.Identity.IsAuthenticated == true)
            {
                var idp = User.FindFirst(JwtClaimTypes.IdentityProvider)?.Value;
                if (
                    idp != null
                    && idp != IdentityServer4.IdentityServerConstants.LocalIdentityProvider
                )
                {
                    var providerSupportsSignout = await HttpContext.GetSchemeSupportsSignOutAsync(
                        idp
                    );
                    if (providerSupportsSignout)
                    {
                        if (vm.LogoutId == null)
                        {
                            vm.LogoutId = await _interaction.CreateLogoutContextAsync();
                        }

                        vm.ExternalAuthenticationScheme = idp;
                    }
                }
            }

            return vm;
        }

        [HttpGet]
        public ActionResult Register([FromQuery] string redirectUrl)
        {
            ViewBag.redirectUrl = redirectUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterSubmit(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return Redirect(model.ReturnUrl);
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }
    }
}
