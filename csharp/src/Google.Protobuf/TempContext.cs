#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2015 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using System;
using System.Collections.Generic;
using System.IO;

namespace Google.Protobuf
{
    /// <summary>
    /// Extension methods on <see cref="IMessage"/> and <see cref="IMessage{T}"/>.
    /// </summary>
    public static class TempContext
    {
        public static MemoryStream memoryStream { get; set; }
        public static CodedInputStream codedInputStream { get; private set; } = new CodedInputStream(new byte[0]);
        public static CodedOutputStream codedOutputStream { get; private set; } = new CodedOutputStream(new byte[0]);
        public static List<Memory<byte>> chunks { get; private set; } = new List<Memory<byte>>();
        public static Func<ReadOnlyMemory<byte>, ByteString> byteStringFactory { get; set; }

        public static Memory<byte> AllocBytes(int size)
        {
            if (memoryStream == null)
            {
                byte[] bytes = new byte[size];
                return bytes;
            }
            else
            {
                memoryStream.SetLength(memoryStream.Position + size);
                Memory<byte> bytes = memoryStream.GetBuffer().AsMemory((int)memoryStream.Position, size);
                memoryStream.Position += size;
                return bytes;
            }
        }
    }
}
