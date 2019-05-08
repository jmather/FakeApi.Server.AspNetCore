using System;
using System.Collections.Generic;
using System.Security.Claims;
using FakeApi.Server.AspNetCore.Controllers;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Controllers
{
    public class MetaControllerTest
    {
        private readonly Uri _metaUri = new Uri("http://127.0.0.1:3000/");
        
        private Mock<IDataService> _dataService;
        private MetaController _controller;
        
        private void Setup()
        {
            _dataService = new Mock<IDataService>();
            _controller = new MetaController(_dataService.Object)
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

        [Fact]
        public void TestRegister()
        {
            Setup();
            
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

            _dataService.Setup(o => o.Register(userInfo)).Returns(user);

            var result = (CreatedResult) _controller.Register(userInfo);

            Assert.Equal(201, result.StatusCode);
            Assert.Equal(_metaUri.ToString(), result.Location);
            Assert.IsType<User>(result.Value);
        }
        
        [Fact]
        public void TestRegisterFailsWithNoUserInfo()
        {
            Setup();
            
            SetupRegisterCall();
            
            var result = (BadRequestObjectResult) _controller.Register(null);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal("No User Information Provided", result.Value);
        }
        
        [Fact]
        public void TestRecord()
        {
            Setup();
            
            var user = SetupRecordCall();

            var endpoint = CreateExampleEndpoint();

            _dataService
                .Setup(ds => ds.RecordEndpoint(It.IsAny<string>(), endpoint))
                .Returns(true);
            
            var result = (CreatedResult) _controller.Record(endpoint);

            Assert.Equal(201, result.StatusCode);
            Assert.Equal(_metaUri.ToString(), result.Location);
            Assert.IsType<FakeEndpoint>(result.Value);
        }

        [Fact]
        public void TestRecordRequiresEndpoint()
        {
            Setup();
            
            SetupRecordCall();
            
            var result = (BadRequestObjectResult) _controller.Record(null);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal("No Endpoint Provided", result.Value);
        }
        
        [Fact]
        public void TestRecordDoesNotLetYouRegisterAnEndpointTwice()
        {
            Setup();
            
            var endpoint = CreateExampleEndpoint();

            _dataService
                .Setup(ds => ds.RecordEndpoint(It.IsAny<string>(), endpoint))
                .Returns(false);
            
            var result = (ConflictObjectResult) _controller.Record(endpoint);

            Assert.Equal(409, result.StatusCode);
            Assert.Equal("Matching endpoint already recorded.", result.Value);
        }

        private void SetupRegisterCall()
        {
            _controller.Request.Method = HttpMethods.Post;
            _controller.Request.Headers.Add(MetaController.FakeApiActionHeader, MetaController.FakeApiAction.Register.ToString());
        }

        private User SetupRecordCall()
        {
            var user = default(User);
            
            _controller.Request.Method = HttpMethods.Put;
            _controller.Request.Headers.Add(MetaController.FakeApiActionHeader, MetaController.FakeApiAction.Record.ToString());

            return user;
        }
        
        private static FakeEndpoint CreateExampleEndpoint()
        {
            return new FakeEndpoint
            {
                Method = FakeEndpointMethod.Get,
                Path = "/hello-world",
                ResponseMode = ResponseMode.Incremental,
                Responses = new List<FakeEndpointResponse>
                {
                    new FakeEndpointResponse
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