// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace bedrock_frontend;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        string? baseUrl = Environment.GetEnvironmentVariable("COGNITO_BASE_URL");
        string? logOutUrl = Environment.GetEnvironmentVariable("COGNITO_LOGOUT_URL");
        string? metadataAddress = Environment.GetEnvironmentVariable("COGNITO_METADATA_ADDRESS");
        string? clientId = Environment.GetEnvironmentVariable("COGNITO_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("COGNITO_CLIENT_SECRET");

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        }).AddCookie(options =>
        {
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.Cookie.MaxAge = options.ExpireTimeSpan;
            options.SlidingExpiration = true;
        })
        .AddOpenIdConnect(options =>
        {
            options.ResponseType = "code";
            options.MetadataAddress = metadataAddress;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.SaveTokens = true;
            options.Scope.Add("email");
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.GetClaimsFromUserInfoEndpoint = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                RoleClaimType = "cognito:groups"
            };
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProviderForSignOut = (context) =>
                {
                    var logoutUri = logOutUrl + $"?client_id={clientId}&logout_uri={baseUrl}";
                    context.Response.Redirect(logoutUri);
                    context.HandleResponse();
                    return Task.CompletedTask;
                }
            };
        });

        services.AddResponseCompression();
        services.AddAuthorization();
        services.AddRazorPages();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseResponseCompression();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
        });
    }
}
