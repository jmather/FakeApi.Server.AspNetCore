using System;
using System.Collections.Generic;
using FakeApi.Server.AspNetCore.Controllers;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Endpoint = FakeApi.Server.AspNetCore.Models.Endpoint;

namespace FakeApi.Server.AspNetCore.Test.Controllers
{
    public class MetaControllerTest
    {
        private readonly Uri _metaUri = new Uri("http://127.0.0.1:3000/");
        
        private Mock<IUserManager> _userManager;
        private MetaController _controller;
        
        [SetUp]
        public void Setup()
        {
            _userManager = new Mock<IUserManager>();
            _controller = new MetaController(_userManager.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _controller.Request.Scheme = _metaUri.Scheme;
            _controller.Request.Host = HostString.FromUriComponent(_metaUri);
            _controller.Request.Path = PathString.FromUriComponent(_metaUri);
        }

        [Test]
        public void TestRegister()
        {
            SetupRegisterCall();

            var externalId = "test";

            var user = new User()
            {
                Username = "a",
                Password = "b",
                ExternalId = externalId,
            };
            
            var userInfo = new UserInfo
            {
                ExternalId = externalId,
            };

            _userManager.Setup(o => o.Register(userInfo)).Returns(user);

            var result = (CreatedResult) _controller.Register(userInfo);

            Assert.AreEqual(201, result.StatusCode);
            Assert.AreEqual(_metaUri.ToString(), result.Location);
            Assert.IsInstanceOf<User>(result.Value);
        }
        
        [Test]
        public void TestRegisterFailsWithNoUserInfo()
        {
            SetupRegisterCall();
            
            var result = (BadRequestObjectResult) _controller.Register(null);

            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("No User Information Provided", result.Value);
        }
        
        [Test]
        public void TestRecord()
        {
            var user = SetupRecordCall(true, true);

            var endpoint = CreateExampleEndpoint();

            var result = (CreatedResult) _controller.Record(endpoint);

            Assert.AreEqual(201, result.StatusCode);
            Assert.AreEqual(_metaUri.ToString(), result.Location);
            Assert.IsInstanceOf<Endpoint>(result.Value);
        }

        [Test]
        public void TestRecordRequiresEndpoint()
        {
            SetupRecordCall(false);
            
            var result = (BadRequestObjectResult) _controller.Record(null);

            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("No Endpoint Provided", result.Value);
        }

        [Test]
        public void TestRecordRequiresAuthentication()
        {
            SetupRecordCall(false);

            var endpoint = CreateExampleEndpoint();
            
            var result = (UnauthorizedResult) _controller.Record(endpoint);

            Assert.AreEqual(401, result.StatusCode);
        }

        [Test]
        public void TestRecordRequiresValidUser()
        {
            var user = SetupRecordCall(true, false);
            
            var endpoint = CreateExampleEndpoint();
            
            var result = (UnauthorizedResult) _controller.Record(endpoint);

            Assert.AreEqual(401, result.StatusCode);
        }

        [Test]
        public void TestRecordDoesNotLetYouRegisterAnEndpointTwice()
        {
            var user = SetupRecordCall(true, true);

            var endpoint = CreateExampleEndpoint();
            
            _controller.Record(endpoint);
            var result = (ConflictObjectResult) _controller.Record(endpoint);

            Assert.AreEqual(409, result.StatusCode);
            Assert.AreEqual("Matching endpoint already recorded.", result.Value);
        }

        private void SetupRegisterCall()
        {
            _controller.Request.Method = HttpMethods.Post;
            _controller.Request.Headers.Add(MetaController.FakeApiActionHeader, FakeApiControllerBase.FakeApiAction.Register.ToString());
        }

        private User SetupRecordCall(bool withAuth, bool setupUserManager = false)
        {
            var user = default(User);
            
            if (withAuth)
            {
                user = new User
                {
                    Username = "a",
                    Password = "b",
                };

                if (setupUserManager)
                {
                    _userManager.Setup(o => o.GetUser(user.Username)).Returns(user);
                }
            
                _controller.Request.Headers.Add(FakeApiControllerBase.AuthorizationHeader, $"Basic {user.ApiToken}");
            }
            
            _controller.Request.Method = HttpMethods.Put;
            _controller.Request.Headers.Add(MetaController.FakeApiActionHeader, FakeApiControllerBase.FakeApiAction.Record.ToString());

            return user;
        }
        
        private static Endpoint CreateExampleEndpoint()
        {
            return new Endpoint
            {
                Method = EndpointMethod.Get,
                Path = "/hello-world",
                ResponseMode = ResponseMode.Incremental,
                Responses = new List<EndpointResponse>
                {
                    new EndpointResponse
                    {
                        Status = 200,
                        Content = "Hello World!",
                        ContentType = "text/plain",
                        Delay = 0,
                    }
                }
            };
        }
    }
}