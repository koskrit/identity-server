using System.Collections.Generic;
using IdentityServer4;
using IdentityServer4.Models;

namespace IdentityServerBackend
{
    public static class Config
    {
        public static IEnumerable<ApiResource> ApiResources =>
            new List<ApiResource> { new ApiResource("projects-api", "Projects API") };

        public static IEnumerable<IdentityResource> IdentityResources =>
            new List<IdentityResource>
            {
                new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
            };

        public static IEnumerable<ApiScope> ApiScopes =>
            new List<ApiScope> { new ApiScope("projects-api", "Projects API") };

        public static IEnumerable<Client> Clients =>
            new List<Client>
            {
                new Client
                {
                    ClientId = "spa-client",
                    ClientName = "Projects SPA",
                    RequireClientSecret = false,
                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,
                    AllowAccessTokensViaBrowser = true,
                    RequireConsent = false,

                    RedirectUris =
                    {
                        "http://localhost:4200/signin-callback",
                        "http://localhost:4200/assets/silent-callback.html",
                        "http://localhost:4200/register-callback",
                        "https://showcase-cms.netlify.app/signin-callback",
                        "https://showcase-cms.netlify.app/assets/silent-callback.html",
                        "https://showcase-cms.netlify.app/register-callback"
                    },
                    PostLogoutRedirectUris =
                    {
                        "http://localhost:4200/signout-callback",
                        "https://showcase-cms.netlify.app/signout-callback"
                    },
                    AllowedCorsOrigins =
                    {
                        "http://localhost:4200",
                        "https://showcase-cms.netlify.app"
                    },

                    AllowedScopes =
                    {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        "projects-api",
                    },
                    AccessTokenLifetime = 600,
                },
                new Client
                {
                    ClientId = "mvc",
                    ClientName = "MVC Client",
                    AllowedGrantTypes = GrantTypes.Hybrid,

                    ClientSecrets = { new Secret("secret".Sha256()) },

                    RedirectUris = { "http://localhost:4201/signin-oidc" },
                    PostLogoutRedirectUris = { "http://localhost:4201/signout-callback-oidc" },

                    AllowedScopes =
                    {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile
                    },
                    AllowOfflineAccess = true
                }
            };
    }
}
