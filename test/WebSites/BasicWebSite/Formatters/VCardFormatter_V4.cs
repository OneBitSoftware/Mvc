// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BasicWebSite.Models;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace BasicWebSite.Formatters
{
    /// <summary>
    /// Provides contact information of a person through VCard format.
    /// In version 4.0 of VCard format, Gender is a supported property.
    /// </summary>
    public class VCardFormatter_V4 : OutputFormatter
    {
        public VCardFormatter_V4()
        {
            SupportedEncodings.Add(Encoding.UTF8);
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/vcard;version=v4.0"));
        }

        protected override bool CanWriteType(Type type)
        {
            return typeof(Contact).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo());
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
        {
            var contact = (Contact)context.Object;

            var builder = new StringBuilder();
            builder.AppendLine("BEGIN:VCARD");
            builder.AppendFormat("FN:{0}", contact.Name);
            builder.AppendLine();
            builder.AppendFormat("GENDER:{0}", (contact.Gender == GenderType.Male) ? "M" : "F");
            builder.AppendLine();
            builder.AppendLine("END:VCARD");

            var selectedEncoding = new MediaType(context.ContentType).Encoding ?? Encoding.UTF8;

            await context.HttpContext.Response.WriteAsync(
                builder.ToString(),
                selectedEncoding);
        }
    }
}