﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Internal;
using Microsoft.AspNet.Mvc.Abstractions;
using Microsoft.AspNet.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNet.Mvc.Infrastructure
{
    public class JsonResultExecutorTest
    {
        [Fact]
        public async Task ExecuteAsync_UsesDefaultContentType_IfNoContentTypeSpecified()
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { foo = "abcd" }));

            var context = GetActionContext();

            var result = new JsonResult(new { foo = "abcd" });
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal("application/json; charset=utf-8", context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task ExecuteAsync_NullEncoding_DoesNotSetCharsetOnContentType()
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { foo = "abcd" }));

            var context = GetActionContext();

            var result = new JsonResult(new { foo = "abcd" });
            result.ContentType = "text/json";
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal("text/json", context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task ExecuteAsync_SetsContentTypeAndEncoding()
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { foo = "abcd" }));

            var context = GetActionContext();

            var result = new JsonResult(new { foo = "abcd" });
            result.ContentType = new MediaTypeHeaderValue("text/json")
            {
                Encoding = Encoding.ASCII
            }.ToString();
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal("text/json; charset=us-ascii", context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task ExecuteAsync_NoResultContentTypeSet_UsesResponseContentType_AndSuppliedEncoding()
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { foo = "abcd" }));
            var expectedContentType = "text/foo; p1=p1-value; charset=us-ascii";

            var context = GetActionContext();
            context.HttpContext.Response.ContentType = expectedContentType;

            var result = new JsonResult(new { foo = "abcd" });
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal(expectedContentType, context.HttpContext.Response.ContentType);
        }

        [Theory]
        [InlineData("text/foo", "text/foo")]
        [InlineData("text/foo; p1=p1-value", "text/foo; p1=p1-value")]
        public async Task ExecuteAsync_NoResultContentTypeSet_UsesDefaultEncoding_DoesNotSetCharset(
            string responseContentType,
            string expectedContentType)
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { foo = "abcd" }));

            var context = GetActionContext();
            context.HttpContext.Response.ContentType = responseContentType;

            var result = new JsonResult(new { foo = "abcd" });
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal(expectedContentType, context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task ExecuteAsync_UsesPassedInSerializerSettings()
        {
            // Arrange
            var expected = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                new { foo = "abcd" },
                Formatting.Indented));

            var context = GetActionContext();

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Formatting = Formatting.Indented;

            var result = new JsonResult(new { foo = "abcd" }, serializerSettings);
            var executor = CreateExcutor();

            // Act
            await executor.ExecuteAsync(context, result);

            // Assert
            var written = GetWrittenBytes(context.HttpContext);
            Assert.Equal(expected, written);
            Assert.Equal("application/json; charset=utf-8", context.HttpContext.Response.ContentType);
        }

        private static JsonResultExecutor CreateExcutor()
        {
            return new JsonResultExecutor(
                new TestHttpResponseStreamWriterFactory(),
                NullLogger<JsonResultExecutor>.Instance,
                new TestOptionsManager<MvcJsonOptions>());
        }

        private static HttpContext GetHttpContext()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            httpContext.RequestServices = services.BuildServiceProvider();

            return httpContext;
        }

        private static ActionContext GetActionContext()
        {
            return new ActionContext(GetHttpContext(), new RouteData(), new ActionDescriptor());
        }

        private static byte[] GetWrittenBytes(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            return Assert.IsType<MemoryStream>(context.Response.Body).ToArray();
        }
    }
}
