using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Http;
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
        public async Task TestRegister()
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

            _dataService.Setup(o => o.Register(It.IsAny<UserInfo>(), It.IsAny<long>())).Returns(user);
            SetBody("{\"external_id\":\"test\"}");

            await _controller.Register();

            Assert.Equal(201, _controller.Response.StatusCode);
            Assert.Equal(_metaUri.ToString(), _controller.Response.Headers.Location.ToString());
        }
        
        [Fact]
        public async Task TestRegisterFailsWithNoUserInfo()
        {
            Setup();
            
            SetupRegisterCall();
            SetBody("");
            
            await _controller.Register();

            Assert.Equal(400, _controller.Response.StatusCode);
        }
        
        [Fact]
        public async Task TestRecord()
        {
            Setup();
            
            SetupRecordCall();
            SetBody("{\"method\":\"get\",\"path\":\"/hello-world\",\"responses\":[{\"status\":200,\"content\":\"Hello World!\",\"content_type\":\"text/plain\"}]}");

            _dataService
                .Setup(ds => ds.RecordEndpoint(It.IsAny<string>(), It.IsAny<FakeEndpoint>()))
                .Returns(true);
            
            await _controller.Record();

            Assert.Equal(201, _controller.Response.StatusCode);
            Assert.Equal(_metaUri.ToString(), _controller.Response.Headers.Location.ToString());
        }

        [Fact]
        public async Task TestRecordRequiresEndpoint()
        {
            Setup();
            
            SetupRecordCall();
            SetBody("");
            
            await _controller.Record();

            Assert.Equal(400, _controller.Response.StatusCode);
        }
        
        [Fact]
        public async Task TestRecordDoesNotLetYouRegisterAnEndpointTwice()
        {
            Setup();
            SetupRecordCall();
            SetBody("{\"method\":\"get\",\"path\":\"/hello-world\",\"responses\":[{\"status\":200,\"content\":\"Hello World!\",\"content_type\":\"text/plain\"}]}");

            _dataService
                .Setup(ds => ds.RecordEndpoint(It.IsAny<string>(), It.IsAny<FakeEndpoint>()))
                .Returns(false);
            
            var result = await _controller.Record();

            var content = (ContentResult) result;
            Assert.Equal(409, content.StatusCode);
            Assert.Equal("Conflict", content.Content);
        }

        private void SetupRegisterCall()
        {
            _controller.Request.Method = HttpMethods.Post;
            _controller.Request.Headers[MetaController.FakeApiActionHeader] = MetaController.FakeApiAction.Register.ToString();
        }

        private User SetupRecordCall()
        {
            var user = default(User);
            
            _controller.Request.Method = HttpMethods.Put;
            _controller.Request.Headers[MetaController.FakeApiActionHeader] = MetaController.FakeApiAction.Record.ToString();

            return user;
        }
        
        private void SetBody(string json)
        {
            _controller.HttpContext.Items[RequestBodyItem.Key] = Encoding.UTF8.GetBytes(json);
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