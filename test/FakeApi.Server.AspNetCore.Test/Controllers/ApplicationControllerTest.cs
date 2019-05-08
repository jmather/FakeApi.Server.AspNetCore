using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Controllers;
using FakeApi.Server.AspNetCore.Exceptions;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Controllers
{
    public class ApplicationControllerTest
    {
        private readonly Uri _callUri = new Uri("http://127.0.0.1:3000/hello-world");
        
        private Mock<IDataService> _dataService;
        private ApplicationController _controller;

        private void Setup()
        {
            _dataService = new Mock<IDataService>();
            _controller = new ApplicationController(_dataService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _controller.Request.Scheme = _callUri.Scheme;
            _controller.Request.Host = HostString.FromUriComponent(_callUri);
            _controller.Request.Path = PathString.FromUriComponent(_callUri);
        }

        [Fact]
        public async Task TestGetResponseSuccess()
        {
            Setup();
            
            _controller.Request.Method = HttpMethods.Get;

            var response = new FakeEndpointResponse
            {
                Status = 200,
                Content = "Hello World!",
                ContentType = "text/plain",
            };
            
            _dataService
                .Setup(o => o.GetEndpointResponse(It.IsAny<string>(), _controller.Request))
                .ReturnsAsync(response);

            var result = (ContentResult) await _controller.HandleRequest();

            Assert.Equal(response.Status, result.StatusCode);
            Assert.Equal(response.Content, result.Content);
            Assert.Equal(response.ContentType, result.ContentType);
        }

        [Fact]
        public async Task TestGetResponseReturnsBadRequestOnMultipleMatch()
        {
            Setup();
            
            _controller.Request.Method = HttpMethods.Get;

            var ex = new MultipleMatchException(new List<FakeEndpoint>(), 0, new List<EndpointMatchingService.ScoredEndpoint>());

            _dataService
                .Setup(o => o.GetEndpointResponse(It.IsAny<string>(), _controller.Request))
                .ThrowsAsync(ex);

            var result = (ObjectResult) await _controller.HandleRequest();

            Assert.Equal(500, result.StatusCode);
            Assert.IsType<ErrorDetails>(result.Value);
            var details = (ErrorDetails) result.Value;
            
            Assert.Equal(MultipleMatchException.MessageText, details.Message);
        }
    }
}