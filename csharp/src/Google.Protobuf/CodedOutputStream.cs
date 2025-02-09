#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using System;
using System.IO;
using System.Security;

namespace Google.Protobuf
{
    /// <summary>
    /// Encodes and writes protocol message fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to write appropriate
    /// primitives to the stream. It effectively encapsulates the lowest
    /// levels of protocol buffer format. Unlike some other implementations,
    /// this does not include combined "write tag and value" methods. Generated
    /// code knows the exact byte representations of the tags they're going to write,
    /// so there's no need to re-encode them each time. Manually-written code calling
    /// this class should just call one of the <c>WriteTag</c> overloads before each value.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <c>RepeatedField&lt;T&gt;</c>
    /// and <c>MapField&lt;TKey, TValue&gt;</c> to serialize such fields.
    /// </para>
    /// </remarks>
    [SecuritySafeCritical]
    public sealed partial class CodedOutputStream : IDisposable
    {
        /// <summary>
        /// The buffer size used by CreateInstance(Stream).
        /// </summary>
        public static readonly int DefaultBufferSize = 4096;

        private bool leaveOpen;
        private Memory<byte> buffer;
        private WriterInternalState state;

        private Stream output;

        #region Construction
        /// <summary>
        /// Creates a new CodedOutputStream that writes directly to the given
        /// byte array. If more bytes are written than fit in the array,
        /// OutOfSpaceException will be thrown.
        /// </summary>
        public CodedOutputStream(Memory<byte> flatArray) : this(flatArray, 0, flatArray.Length)
        {
        }

        /// <summary>
        /// Creates a new CodedOutputStream that writes directly to the given
        /// byte array slice. If more bytes are written than fit in the array,
        /// OutOfSpaceException will be thrown.
        /// </summary>
        private CodedOutputStream(Memory<byte> buffer, int offset, int length)
        {
            Initialize(buffer, offset, length);
        }

        private CodedOutputStream(Stream output, byte[] buffer, bool leaveOpen)
        {
            Initialize(output, buffer, leaveOpen);
        }

        /// <summary>
        /// Creates a new <see cref="CodedOutputStream" /> which write to the given stream, and disposes of that
        /// stream when the returned <c>CodedOutputStream</c> is disposed.
        /// </summary>
        /// <param name="output">The stream to write to. It will be disposed when the returned <c>CodedOutputStream is disposed.</c></param>
        public CodedOutputStream(Stream output) : this(output, DefaultBufferSize, false)
        {
        }

        /// <summary>
        /// Creates a new CodedOutputStream which write to the given stream and uses
        /// the specified buffer size.
        /// </summary>
        /// <param name="output">The stream to write to. It will be disposed when the returned <c>CodedOutputStream is disposed.</c></param>
        /// <param name="bufferSize">The size of buffer to use internally.</param>
        public CodedOutputStream(Stream output, int bufferSize) : this(output, new byte[bufferSize], false)
        {
        }

        /// <summary>
        /// Creates a new CodedOutputStream which write to the given stream.
        /// </summary>
        /// <param name="output">The stream to write to.</param>
        /// <param name="leaveOpen">If <c>true</c>, <paramref name="output"/> is left open when the returned <c>CodedOutputStream</c> is disposed;
        /// if <c>false</c>, the provided stream is disposed as well.</param>
        public CodedOutputStream(Stream output, bool leaveOpen) : this(output, DefaultBufferSize, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a new CodedOutputStream which write to the given stream and uses
        /// the specified buffer size.
        /// </summary>
        /// <param name="output">The stream to write to.</param>
        /// <param name="bufferSize">The size of buffer to use internally.</param>
        /// <param name="leaveOpen">If <c>true</c>, <paramref name="output"/> is left open when the returned <c>CodedOutputStream</c> is disposed;
        /// if <c>false</c>, the provided stream is disposed as well.</param>
        public CodedOutputStream(Stream output, int bufferSize, bool leaveOpen) : this(output, new byte[bufferSize], leaveOpen)
        {
        }

        public void Initialize(Memory<byte> flatArray)
        {
            Initialize(flatArray, 0, flatArray.Length);
        }

        public void Initialize(Memory<byte> buffer, int offset, int length)
        {
            this.output = null;
            this.buffer = buffer;
            this.state.position = offset;
            this.state.limit = offset + length;
            WriteBufferHelper.Initialize(this, out this.state.writeBufferHelper);
            leaveOpen = true; // Simple way of avoiding trying to dispose of a null reference
        }

        public void Initialize(Stream output, byte[] buffer, bool leaveOpen)
        {
            this.output = ProtoPreconditions.CheckNotNull(output, nameof(output));
            this.buffer = buffer;
            this.state.position = 0;
            this.state.limit = buffer.Length;
            WriteBufferHelper.Initialize(this, out this.state.writeBufferHelper);
            this.leaveOpen = leaveOpen;
        }

        public void Initialize(Stream output)
        {
            Initialize(output, DefaultBufferSize, false);
        }

        public void Initialize(Stream output, int bufferSize)
        {
            Initialize(output, new byte[bufferSize], false);
        }

        public void Initialize(Stream output, bool leaveOpen)
        {
            Initialize(output, DefaultBufferSize, leaveOpen);
        }

        public void Initialize(Stream output, int bufferSize, bool leaveOpen)
        {
            Initialize(output, new byte[bufferSize], leaveOpen);
        }
        #endregion

        /// <summary>
        /// Returns the current position in the stream, or the position in the output buffer
        /// </summary>
        public long Position
        {
            get
            {
                if (output != null)
                {
                    return output.Position + state.position;
                }
                return state.position;
            }
        }

        /// <summary>
        /// Configures whether or not serialization is deterministic.
        /// </summary>
        /// <remarks>
        /// Deterministic serialization guarantees that for a given binary, equal messages (defined by the
        /// equals methods in protos) will always be serialized to the same bytes. This implies:
        /// <list type="bullet">
        /// <item><description>Repeated serialization of a message will return the same bytes.</description></item>
        /// <item><description>Different processes of the same binary (which may be executing on different machines)
        /// will serialize equal messages to the same bytes.</description></item>
        /// </list>
        /// Note the deterministic serialization is NOT canonical across languages; it is also unstable
        /// across different builds with schema changes due to unknown fields. Users who need canonical
        /// serialization, e.g. persistent storage in a canonical form, fingerprinting, etc, should define
        /// their own canonicalization specification and implement the serializer using reflection APIs
        /// rather than relying on this API.
        /// Once set, the serializer will: (Note this is an implementation detail and may subject to
        /// change in the future)
        /// <list type="bullet">
        /// <item><description>Sort map entries by keys in lexicographical order or numerical order. Note: For string
        /// keys, the order is based on comparing the UTF-16 code unit value of each character in the strings.
        /// The order may be different from the deterministic serialization in other languages where
        /// maps are sorted on the lexicographical order of the UTF8 encoded keys.</description></item>
        /// </list>
        /// </remarks>
        public bool Deterministic { get; set; }

        #region Writing of values (not including tags)

        /// <summary>
        /// Writes a double field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteDouble(double value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteDouble(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a float field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFloat(float value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteFloat(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a uint64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteUInt64(ulong value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteUInt64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an int64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteInt64(long value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteInt64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an int32 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteInt32(int value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteInt32(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a fixed64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFixed64(ulong value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteFixed64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a fixed32 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFixed32(uint value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteFixed32(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a bool field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteBool(bool value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteBool(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a string field value, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteString(string value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteString(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a message, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteMessage(IMessage value)
        {
            // TODO: if the message doesn't implement IBufferMessage (and thus does not provide the InternalWriteTo method),
            // what we're doing here works fine, but could be more efficient.
            // For now, this inefficiency is fine, considering this is only a backward-compatibility scenario (and regenerating the code fixes it).
            var span = buffer.Span;
            WriteContext.Initialize(ref span, ref state, out WriteContext ctx);
            try
            {
                WritingPrimitivesMessages.WriteMessage(ref ctx, value);
            }
            finally
            {
                ctx.CopyStateTo(this);
            }
        }

        /// <summary>
        /// Writes a message, without a tag, to the stream.
        /// Only the message data is written, without a length-delimiter.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteRawMessage(IMessage value)
        {
            // TODO: if the message doesn't implement IBufferMessage (and thus does not provide the InternalWriteTo method),
            // what we're doing here works fine, but could be more efficient.
            // For now, this inefficiency is fine, considering this is only a backward-compatibility scenario (and regenerating the code fixes it).
            var span = buffer.Span;
            WriteContext.Initialize(ref span, ref state, out WriteContext ctx);
            try
            {
                WritingPrimitivesMessages.WriteRawMessage(ref ctx, value);
            }
            finally
            {
                ctx.CopyStateTo(this);
            }
        }

        /// <summary>
        /// Writes a group, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteGroup(IMessage value)
        {
            var span = buffer.Span;
            WriteContext.Initialize(ref span, ref state, out WriteContext ctx);
            try
            {
                WritingPrimitivesMessages.WriteGroup(ref ctx, value);
            }
            finally
            {
                ctx.CopyStateTo(this);
            }
        }

        /// <summary>
        /// Write a byte string, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteBytes(ByteString value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteBytes(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a uint32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteUInt32(uint value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteUInt32(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an enum value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteEnum(int value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteEnum(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an sfixed32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSFixed32(int value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteSFixed32(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an sfixed64 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSFixed64(long value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteSFixed64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an sint32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSInt32(int value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteSInt32(ref span, ref state, value);
        }

        /// <summary>
        /// Writes an sint64 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSInt64(long value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteSInt64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes a length (in bytes) for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This method simply writes a rawint, but exists for clarity in calling code.
        /// </remarks>
        /// <param name="length">Length value, in bytes.</param>
        public void WriteLength(int length)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteLength(ref span, ref state, length);
        }

        #endregion

        #region Raw tag writing
        /// <summary>
        /// Encodes and writes a tag.
        /// </summary>
        /// <param name="fieldNumber">The number of the field to write the tag for</param>
        /// <param name="type">The wire format type of the tag to write</param>
        public void WriteTag(int fieldNumber, WireFormat.WireType type)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteTag(ref span, ref state, fieldNumber, type);
        }

        /// <summary>
        /// Writes an already-encoded tag.
        /// </summary>
        /// <param name="tag">The encoded tag</param>
        public void WriteTag(uint tag)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteTag(ref span, ref state, tag);
        }

        /// <summary>
        /// Writes the given single-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The encoded tag</param>
        public void WriteRawTag(byte b1)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawTag(ref span, ref state, b1);
        }

        /// <summary>
        /// Writes the given two-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawTag(ref span, ref state, b1, b2);
        }

        /// <summary>
        /// Writes the given three-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawTag(ref span, ref state, b1, b2, b3);
        }

        /// <summary>
        /// Writes the given four-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        /// <param name="b4">The fourth byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3, byte b4)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawTag(ref span, ref state, b1, b2, b3, b4);
        }

        /// <summary>
        /// Writes the given five-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        /// <param name="b4">The fourth byte of the encoded tag</param>
        /// <param name="b5">The fifth byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3, byte b4, byte b5)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawTag(ref span, ref state, b1, b2, b3, b4, b5);
        }
        #endregion

        #region Underlying writing primitives

        /// <summary>
        /// Writes a 32 bit value as a varint. The fast route is taken when
        /// there's enough buffer space left to whizz through without checking
        /// for each byte; otherwise, we resort to calling WriteRawByte each time.
        /// </summary>
        internal void WriteRawVarint32(uint value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawVarint32(ref span, ref state, value);
        }

        internal void WriteRawVarint64(ulong value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawVarint64(ref span, ref state, value);
        }

        internal void WriteRawLittleEndian32(uint value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawLittleEndian32(ref span, ref state, value);
        }

        internal void WriteRawLittleEndian64(ulong value)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawLittleEndian64(ref span, ref state, value);
        }

        /// <summary>
        /// Writes out an array of bytes.
        /// </summary>
        internal void WriteRawBytes(byte[] value)
        {
            WriteRawBytes(value, 0, value.Length);
        }

        /// <summary>
        /// Writes out part of an array of bytes.
        /// </summary>
        internal void WriteRawBytes(byte[] value, int offset, int length)
        {
            var span = buffer.Span;
            WritingPrimitives.WriteRawBytes(ref span, ref state, value, offset, length);
        }

        #endregion

        /// <summary>
        /// Indicates that a CodedOutputStream wrapping a flat byte array
        /// ran out of space.
        /// </summary>
        public sealed class OutOfSpaceException : IOException
        {
            internal OutOfSpaceException()
                : base("CodedOutputStream was writing to a flat byte array and ran out of space.")
            {
            }
        }

        /// <summary>
        /// Flushes any buffered data and optionally closes the underlying stream, if any.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, any underlying stream is closed by this method. To configure this behaviour,
        /// use a constructor overload with a <c>leaveOpen</c> parameter. If this instance does not
        /// have an underlying stream, this method does nothing.
        /// </para>
        /// <para>
        /// For the sake of efficiency, calling this method does not prevent future write calls - but
        /// if a later write ends up writing to a stream which has been disposed, that is likely to
        /// fail. It is recommend that you not call any other methods after this.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            Flush();
            if (!leaveOpen)
            {
                output.Dispose();
            }
        }

        /// <summary>
        /// Flushes any buffered data to the underlying stream (if there is one).
        /// </summary>
        public void Flush()
        {
            var span = buffer.Span;
            WriteBufferHelper.Flush(ref span, ref state);
        }

        /// <summary>
        /// Verifies that SpaceLeft returns zero. It's common to create a byte array
        /// that is exactly big enough to hold a message, then write to it with
        /// a CodedOutputStream. Calling CheckNoSpaceLeft after writing verifies that
        /// the message was actually as big as expected, which can help finding bugs.
        /// </summary>
        public void CheckNoSpaceLeft()
        {
            WriteBufferHelper.CheckNoSpaceLeft(ref state);
        }

        /// <summary>
        /// If writing to a flat array, returns the space left in the array. Otherwise,
        /// throws an InvalidOperationException.
        /// </summary>
        public int SpaceLeft => WriteBufferHelper.GetSpaceLeft(ref state);

        internal Memory<byte> InternalBuffer => buffer;

        internal Stream InternalOutputStream => output;

        internal ref WriterInternalState InternalState => ref state;
    }
}
