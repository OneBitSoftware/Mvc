﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.AspNet.Mvc.ViewFeatures.Buffer
{
    /// <summary>
    /// Creates and manages the lifetime of <see cref="ViewBufferValue[]"/> instances.
    /// </summary>
    public interface IViewBufferScope
    {
        /// <summary>
        /// Gets a <see cref="ViewBufferValue[]"/>.
        /// </summary>
        /// <returns>The <see cref="ViewBufferValue[]"/>.</returns>
        ViewBufferValue[] GetSegment();

        /// <summary>
        /// Creates a <see cref="ViewBufferTextWriter"/> that will delegate to the provided
        /// <paramref name="writer"/>.
        /// </summary>
        /// <param name="writer">The <see cref="TextWriter"/>.</param>
        /// <returns>A <see cref="ViewBufferTextWriter"/>.</returns>
        ViewBufferTextWriter CreateWriter(TextWriter writer);
    }
}
