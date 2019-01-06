using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip.Compression;

#pragma warning disable 1591

namespace GZipPullStream
{
    /// <summary>
    /// <see cref="GZipStream"/> implementation that allows pulling compressed
    /// data from it, instead of it writing compressed data to the base stream.
    /// </summary>
    public class GZipPullStream : DeflatePullStream
    {
        private readonly Crc32 _crc = new Crc32();

        /// <summary>
        /// Create a new <see cref="GZipPullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        public GZipPullStream(Stream stream)
            : this(stream, false)
        {
        }

        /// <summary>
        /// Create a new <see cref="GZipPullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        public GZipPullStream(Stream stream, bool leaveOpen)
            : this(stream, leaveOpen, null, DefaultBufferSize)
        {
        }

        /// <summary>
        /// Create a new <see cref="GZipPullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        /// <param name="compressionLevel">Compression level.</param>
        /// <param name="bufferSize">Buffer size.</param>
        public GZipPullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize)
            : base(stream, leaveOpen, bufferSize, new Deflater(GetCompressionLevel(compressionLevel), true))
        {
        }

        protected override void UpdateInput(int read)
        {
            if (read > 0)
                _crc.Update(new ArraySegment<byte>(_buffer, 0, read));

            base.UpdateInput(read);
        }

        protected override ArraySegment<byte> BuildHeader()
        {
            var time = (int)((DateTime.Now.Ticks - new DateTime(1970, 1, 1).Ticks) / 10000000L);  // Ticks give back 100ns intervals

            byte[] result =
            {
                // The two magic bytes
                GZipConstants.GZIP_MAGIC >> 8, GZipConstants.GZIP_MAGIC & 0xff,

                // The compression type
                Deflater.DEFLATED,

                // The flags (not set)
                0,

                // The modification time
                (byte)time, (byte)(time >> 8),
                (byte)(time >> 16), (byte)(time >> 24),

                // The extra flags
                0,

                // The OS type (unknown)
                255
            };

            return new ArraySegment<byte>(result);
        }

        protected override ArraySegment<byte> BuildFooter()
        {
            var totalIn = (uint)(_deflater.TotalIn & 0xffffffff);
            var crc = (uint)(_crc.Value & 0xffffffff);

            byte[] result;

            unchecked
            {
                result = new[]
                {
                    (byte)crc, (byte)(crc >> 8),
                    (byte)(crc >> 16), (byte)(crc >> 24),

                    (byte)totalIn, (byte)(totalIn >> 8),
                    (byte)(totalIn >> 16), (byte)(totalIn >> 24)
                };
            }

            return new ArraySegment<byte>(result);
        }
    }
}
