using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FakeApi.Server.AspNetCore.Handlers
{
    public class BasicAuthenticationHandler  : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IUserRepository _userRepository;

        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IUserRepository userRepository)
            : base(options, logger, encoder)
        {
            _userRepository = userRepository;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
                return AuthenticateResult.Fail("Missing Authorization Header");

            User user;

            try 
            {
                var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
                if (authHeader.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) == false
                    && authHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == false)
                {
                    return AuthenticateResult.Fail("Unsupported Authorization Scheme");
                }

                var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
                var decoded = Encoding.UTF8.GetString(credentialBytes);
                var split = decoded.IndexOf(':');
                if (split < 0)
                {
                    return AuthenticateResult.Fail("Invalid Authorization Header");
                }

                var username = decoded.Substring(0, split);
                var password = decoded.Substring(split + 1);
                user = await _userRepository.Authenticate(username, password, ServerClock.Current(Context));
            } 
            catch 
            {
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }

            if (user == null)
                return AuthenticateResult.Fail("Invalid Username or Password");

            var claims = new[] { 
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
            };
            
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"FakeAPI\"";
            Response.Headers["Content-Type"] = PlainResponses.PlainContentType;
            var body = Encoding.UTF8.GetBytes("Unauthorized");
            await Response.Body.WriteAsync(body).ConfigureAwait(false);
        }
    }
}