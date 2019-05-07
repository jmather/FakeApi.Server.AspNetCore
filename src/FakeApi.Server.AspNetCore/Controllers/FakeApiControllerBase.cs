using System.Linq;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace FakeApi.Server.AspNetCore.Controllers
{
    public abstract class FakeApiControllerBase : ControllerBase
    {
        private const string AuthorizationHeader = "authorization";
        
        protected readonly UserManager UserManager;

        protected FakeApiControllerBase(UserManager userManager)
        {
            UserManager = userManager;
        }
        
        protected User GetRequestUser()
        {
            var authRequest = GetAuthenticationRequest();

            if (authRequest == null)
            {
                return null;
            }

            var user = UserManager.GetUser(authRequest.Username);

            if (user == null || user.Password != authRequest.Password)
            {
                return null;
            }

            return user;
        }

        protected AuthenticationRequest GetAuthenticationRequest()
        {
            var authHeaderValue = HttpContext.Request.Headers
                .Where(h => h.Key.ToLower() == AuthorizationHeader)
                .Select(h => h.Value.ToString())
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(authHeaderValue))
            {
                return null;
            }
            
            var protocolAndValue = authHeaderValue.Split(" ", 2);

            if (protocolAndValue.Length != 2)
            {
                return null;
            }

            var data = System.Convert.FromBase64String(protocolAndValue[1]);
            var authHeader = System.Text.Encoding.ASCII.GetString(data);
            
            var userAndPass = authHeader.Split(":", 2);

            if (userAndPass.Length != 2)
            {
                return null;
            }
            
            return new AuthenticationRequest()
            {
                Username = userAndPass[0],
                Password = userAndPass[1],
            };
        }

        public enum FakeApiAction
        {
            Register,
            Record
        }
    }
}