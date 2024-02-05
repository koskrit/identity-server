using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using IdentityServer4;
using IdentityServer4.EntityFramework.Stores;
using IdentityServer4.Services;
using IdentityServerBackend.Data;
using IdentityServerBackend.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IdentityServerBackend
{
    public class Startup
    {
        public IWebHostEnvironment Environment { get; }
        public IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment environment, IConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"))
            );

            services
                .AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddCors(options =>
            {
                options.AddPolicy(
                    "CorsPolicy",
                    corsBuilder =>
                    {
                        var allowedOrigins = Configuration
                            .GetSection("AllowedOrigins")
                            .Get<List<string>>();

                        corsBuilder
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .SetIsOriginAllowed(origin => allowedOrigins.Contains(origin))
                            .AllowCredentials();
                    }
                );
            });

            services.AddMvc();

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.MinimumSameSitePolicy = SameSiteMode.None;
                options.Secure = CookieSecurePolicy.Always;
            });

            services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
            services.AddTransient<PasswordHasher<ApplicationUser>>();
            services.AddControllers();

            var builder = services
                .AddIdentityServer(options =>
                {
                    options.Events.RaiseErrorEvents = true;
                    options.Events.RaiseInformationEvents = true;
                    options.Events.RaiseFailureEvents = true;
                    options.Events.RaiseSuccessEvents = true;

                    options.EmitStaticAudienceClaim = true;
                })
                .AddConfigurationStore(options =>
                {
                    options.ConfigureDbContext = builder =>
                    {
                        builder.UseNpgsql(
                            Configuration.GetConnectionString("DefaultConnection"),
                            b => b.MigrationsAssembly("IdentityServerBackend")
                        );
                    };
                })
                .AddPersistedGrantStore<PersistedGrantStore>()
                .AddOperationalStore(options =>
                {
                    options.ConfigureDbContext = builder =>
                    {
                        builder.UseNpgsql(
                            Configuration.GetConnectionString("DefaultConnection"),
                            b => b.MigrationsAssembly("IdentityServerBackend")
                        );
                    };
                })
                .AddAspNetIdentity<ApplicationUser>();

            var certPassword = Configuration.GetValue<string>("CertificateSettings:CertPassword");

            var certFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "SigningKey.pfx"
            );
            var cert = new X509Certificate2(certFilePath, certPassword);

            services
                .AddDataProtection()
                .PersistKeysToDbContext<ApplicationDbContext>()
                .SetApplicationName("IdentityServer");
        }

        public void Configure(IApplicationBuilder app, IServiceScopeFactory serviceProvider)
        {
            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            app.UseStaticFiles();
            app.UseCors("CorsPolicy");

            app.UseRouting();
            app.UseIdentityServer();

            app.UseCookiePolicy();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });

            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<
                    UserManager<ApplicationUser>
                >();

                var user = new ApplicationUser { UserName = "user1", Email = "user1@mail.com", };

                var result = userManager.CreateAsync(user, "Some123!").GetAwaiter().GetResult();
            }
        }
    }
}
