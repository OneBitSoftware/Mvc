// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc.Formatters.Xml;
using Microsoft.AspNet.Testing;
using Microsoft.AspNet.Testing.xunit;
using Xunit;

namespace Microsoft.AspNet.Mvc.FunctionalTests
{
    public class SerializableErrorTests : IClassFixture<MvcTestFixture<XmlFormattersWebSite.Startup>>
    {
        public SerializableErrorTests(MvcTestFixture<XmlFormattersWebSite.Startup> fixture)
        {
            Client = fixture.Client;
        }

        public HttpClient Client { get; }

        public static TheoryData AcceptHeadersData
        {
            get
            {
                var data = new TheoryData<string>
                {
                    "application/xml-xmlser"
                };

                // Avoid aspnet/External#18, a DCS issue on Mono.
                if (!TestPlatformHelper.IsMono)
                {
                    data.Add("application/xml-dcs");
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(AcceptHeadersData))]
        public async Task ModelStateErrors_AreSerialized(string acceptHeader)
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/SerializableError/ModelStateErrors");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));
            var expectedXml = "<Error><key1>key1-error</key1><key2>The input was not valid.</key2></Error>";

            // Act
            var response = await Client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content);
            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Equal(acceptHeader, response.Content.Headers.ContentType.MediaType);
            var responseData = await response.Content.ReadAsStringAsync();
            XmlAssert.Equal(expectedXml, responseData);
        }

        [ConditionalTheory]
        // Mono.Xml2.XmlTextReader.ReadText is unable to read the XML. Is this the same bug as aspnet/External#18???
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "aspnet/External#18, DCS issue on Mono")]
        [InlineData("application/xml-xmlser")]
        [InlineData("application/xml-dcs")]
        public async Task PostedSerializableError_IsBound(string acceptHeader)
        {
            // Arrange
            var expectedXml = "<Error><key1>key1-error</key1><key2>The input was not valid.</key2></Error>";
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/SerializableError/LogErrors");
            request.Content = new StringContent(expectedXml, Encoding.UTF8, acceptHeader);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));

            // Act
            var response = await Client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content);
            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Equal(acceptHeader, response.Content.Headers.ContentType.MediaType);
            var responseData = await response.Content.ReadAsStringAsync();
            XmlAssert.Equal(expectedXml, responseData);
        }

        [ConditionalTheory]
        // Mono.Xml2.XmlTextReader.ReadText is unable to read the XML. Is this the same bug as aspnet/External#18???
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "aspnet/External#18, DCS issue on Mono")]
        [InlineData("application/xml-xmlser")]
        [InlineData("application/xml-dcs")]
        public async Task IsReturnedInExpectedFormat(string acceptHeader)
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Employee xmlns=\"http://schemas.datacontract.org/2004/07/XmlFormattersWebSite.Models\">" +
                "<Id>2</Id><Name>foo</Name></Employee>";
            var expected = "<Error><Id>The field Id must be between 10 and 100.</Id>" +
                "<Name>The field Name must be a string or array type with a minimum " +
                "length of '15'.</Name></Error>";
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/SerializableError/CreateEmployee");
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeader));
            request.Content = new StringContent(input, Encoding.UTF8, "application/xml-dcs");

            // Act
            var response = await Client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseData = await response.Content.ReadAsStringAsync();
            XmlAssert.Equal(expected, responseData);
        }
    }
}
