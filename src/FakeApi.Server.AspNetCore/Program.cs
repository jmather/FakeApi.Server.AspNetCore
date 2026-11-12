using FakeApi.Server.AspNetCore.Handlers;
using FakeApi.Server.AspNetCore.Logging;
using FakeApi.Server.AspNetCore.Repositories;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Conventions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Serialization;

namespace FakeApi.Server.AspNetCore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 3.x throws on startup when no connection string is configured. The 2.x SDK
            // did not, so telemetry stays off unless the host actually configured it.
            var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]
                ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
            {
                var instrumentationKey = builder.Configuration["ApplicationInsights:InstrumentationKey"]
                    ?? builder.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];
                if (string.IsNullOrWhiteSpace(instrumentationKey) == false)
                {
                    appInsightsConnectionString = "InstrumentationKey=" + instrumentationKey;
                }
            }

            if (string.IsNullOrWhiteSpace(appInsightsConnectionString) == false)
            {
                builder.Services.AddApplicationInsightsTelemetry(options =>
                {
                    options.ConnectionString = appInsightsConnectionString;
                });
            }

            builder.Services.AddCors();

            builder.Services.AddSingleton<IUserRepository, UserRepository>();
            builder.Services.AddSingleton<IDataService, DataService>();
            builder.Services.AddSingleton<IEndpointMatchingService, EndpointMatchingService>();

            builder.Services
                .AddControllers(opt =>
                {
                    opt.Conventions.Add(new RequiredHeaderConvention());
                })
                .AddNewtonsoftJson(x =>
                {
                    x.SerializerSettings.ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new SnakeCaseNamingStrategy()
                    };
                });

            builder.AddFakeApiRequestLogging();

            const string authenticationScheme = "BasicAuthentication";
            builder.Services.AddAuthentication(authenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(authenticationScheme, _ => { });

            var app = builder.Build();

            app.UseFakeApiRequestLogging();

            if (app.Environment.IsDevelopment() == false)
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Reflect the request origin. AllowAnyOrigin cannot be combined with credentials.
            app.UseCors(x => x
                .SetIsOriginAllowed(_ => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
