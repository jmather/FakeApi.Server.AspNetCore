using System;
using System.Linq;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace FakeApi.Server.AspNetCore.Controllers
{
    public abstract class FakeApiControllerBase : ControllerBase
    {
        public const string AuthorizationHeader = "Authorization";
        
        protected readonly IUserManager UserManager;

        protected FakeApiControllerBase(IUserManager userManager)
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
                .Where(h => string.Equals(h.Key, AuthorizationHeader, StringComparison.OrdinalIgnoreCase))
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