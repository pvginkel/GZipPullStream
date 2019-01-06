using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GZipPullStream
{
    public class GZipPullStream : DeflatePullStream
    {
        private readonly Crc32 _crc = new Crc32();

        public GZipPullStream(Stream stream)
            : this(stream, false)
        {
        }

        public GZipPullStream(Stream stream, bool leaveOpen)
            : this(stream, leaveOpen, null, DefaultBufferSize)
        {
        }

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
