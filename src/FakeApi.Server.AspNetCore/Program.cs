using FakeApi.Server.AspNetCore.Handlers;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Logging;
using FakeApi.Server.AspNetCore.Repositories;
using FakeApi.Server.AspNetCore.Schema;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Conventions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

            var clock = ServerClock.FromEnvironment();
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton<IUserRepository>(_ => new UserRepository(UserRepository.Config.FromEnvironment()));
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

            app.UseMiddleware<GateMiddleware>();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/fake-api-schema.json", async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers["Content-Type"] = PlainResponses.JsonContentType;
                await context.Response.Body.WriteAsync(SchemaAssets.SchemaJson);
            });
            app.MapGet("/fake-api.openapi.yaml", async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers["Content-Type"] = "application/yaml";
                await context.Response.Body.WriteAsync(SchemaAssets.OpenApiYaml);
            });

            app.MapControllers();

            app.Run();
        }
    }
}
