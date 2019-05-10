using FakeApi.Server.AspNetCore.Handlers;
using FakeApi.Server.AspNetCore.Repositories;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Conventions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Serialization;

namespace FakeApi.Server.AspNetCore
{
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
            services.AddApplicationInsightsTelemetry();
            
            services.AddCors();

            services.AddSingleton<IUserRepository, UserRepository>();
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<IEndpointMatchingService, EndpointMatchingService>();
            
            services
                .AddMvc(opt =>
                {
                    opt.Conventions.Add(new RequiredHeaderConvention());
                })
                .AddJsonOptions(x =>
                {
                    x.SerializerSettings.ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new SnakeCaseNamingStrategy()
                    };
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            var authenticationScheme = "BasicAuthentication";
            services.AddAuthentication(authenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(authenticationScheme, null);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment() == false)
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // global cors policy
            app.UseCors(x => x
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());

            app.UseAuthentication();

            app.UseMvc();
        }
    }
}