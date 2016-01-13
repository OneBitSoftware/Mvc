﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Mvc.ViewFeatures.Buffer
{
    public class ViewBufferTextWriter : TextWriter
    {
        private const int PageSize = 1024;

        private readonly TextWriter _inner;
        private readonly List<char[]> _pages;
        private readonly ArrayPool<char> _pool;

        private int _currentPage;
        private int _currentIndex; // The next 'free' character

        public ViewBufferTextWriter(ArrayPool<char> pool, TextWriter inner)
        {
            _pool = pool;
            _inner = inner;
            _pages = new List<char[]>();
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Flush()
        {
            // Don't do anything. We'll call FlushAsync.
        }

        public override async Task FlushAsync()
        {
            for (var i = 0; i <= _currentPage; i++)
            {
                var page = _pages[i];

                var count = i == _currentPage ? _currentIndex : page.Length;
                await _inner.WriteAsync(page, 0, count);
            }

            _currentPage = 0;
            _currentIndex = 0;
        }

        public override void Write(char value)
        {
            var page = GetCurrentPage();
            page[_currentIndex++] = value;
        }

        public override void Write(char[] buffer)
        {
            Write(buffer, 0, buffer.Length);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            while (count > 0)
            {
                var page = GetCurrentPage();
                var copyLength = page.Length - _currentIndex;
                Debug.Assert(copyLength > 0);

                System.Buffer.BlockCopy(
                    buffer,
                    index,
                    page,
                    _currentIndex,
                    copyLength);

                _currentIndex += copyLength;
                index += copyLength;

                count -= copyLength;
            }
        }

        public override void Write(string value)
        {
            var index = 0;
            var count = value.Length;

            while (count > 0)
            {
                var page = GetCurrentPage();
                var copyLength = page.Length - _currentIndex;
                Debug.Assert(copyLength > 0);

                value.CopyTo(
                    index,
                    page,
                    _currentIndex,
                    copyLength);

                _currentIndex += copyLength;
                index += copyLength;

                count -= copyLength;
            }
        }

        public override Task WriteAsync(char value)
        {
            return _inner.WriteAsync(value);
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            return _inner.WriteAsync(buffer, index, count);
        }

        public override Task WriteAsync(string value)
        {
            return _inner.WriteAsync(value);
        }

        private char[] GetCurrentPage()
        {
            var page = _pages.Count == 0 ? null : _pages[_currentPage];
            if (page == null || _currentIndex == page.Length)
            {
                // Current page is full.
                _currentPage++;
                _currentIndex = 0;

                if (_pages.Count > _currentPage)
                {
                    page = _pages[_currentPage];
                }
                else
                {
                    page = _pool.Rent(PageSize);
                    _pages.Add(page);
                }
            }

            return page;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            for (var i = 0; i < _pages.Count; i++)
            {
                _pool.Return(_pages[i]);
            }
        }
    }
}
