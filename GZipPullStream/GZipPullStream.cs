using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GZipPullStream
{
    public class GZipPullStream : Stream
    {
        private readonly Stream stream;
        private readonly bool leaveOpen;
        private readonly Deflater deflater;
        private readonly byte[] buffer;
        private ArraySegment<byte> available;
        private readonly Crc32 crc = new Crc32();
        private State state;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanTimeout => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public GZipPullStream(Stream stream)
            : this(stream, false)
        {
        }

        public GZipPullStream(Stream stream, bool leaveOpen)
            : this(stream, leaveOpen, null, 4096)
        {
        }

        public GZipPullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize)
        {
            this.stream = stream;
            this.leaveOpen = leaveOpen;
            this.buffer = new byte[bufferSize];
            this.deflater = new Deflater(GetCompressionLevel(compressionLevel), true);
            this.state = State.Header;
        }

        private int GetCompressionLevel(CompressionLevel? compressionLevel)
        {
            switch (compressionLevel)
            {
                case null: return Deflater.DEFAULT_COMPRESSION;
                case CompressionLevel.Optimal: return Deflater.BEST_COMPRESSION;
                case CompressionLevel.Fastest: return Deflater.BEST_SPEED;
                case CompressionLevel.NoCompression: return Deflater.NO_COMPRESSION;
                default: throw new ArgumentOutOfRangeException(nameof(compressionLevel));
            }
        }

        public override void Close()
        {
            if (!leaveOpen)
                stream.Close();

            base.Close();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (TryReadAvailable(buffer, offset, count, out int read))
                    return read;

                UpdateInput(await stream.ReadAsync(this.buffer, 0, this.buffer.Length, cancellationToken));
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                if (TryReadAvailable(buffer, offset, count, out int read))
                    return read;

                UpdateInput(stream.Read(this.buffer, 0, this.buffer.Length));
            }
        }

        private void UpdateInput(int read)
        {
            if (read == 0)
            {
                state = State.Flushing;
                deflater.Finish();
            }
            else
            {
                crc.Update(new ArraySegment<byte>(buffer, 0, read));
                deflater.SetInput(buffer, 0, read);
            }
        }

        private bool TryReadAvailable(byte[] buffer, int offset, int count, out int read)
        {
            if (available.Count > 0)
            {
                read = Math.Min(available.Count, count);
                Array.Copy(available.Array, available.Offset, buffer, offset, read);
                available = new ArraySegment<byte>(available.Array, available.Offset + read, available.Count - read);
                return true;
            }

            if (state == State.Header)
            {
                state = State.Content;
                available = new ArraySegment<byte>(BuildHeader());
                return TryReadAvailable(buffer, offset, count, out read);
            }
            if (state == State.Footer)
            {
                state = State.Finished;
                available = new ArraySegment<byte>(BuildFooter());
                return TryReadAvailable(buffer, offset, count, out read);
            }

            read = 0;

            if (state == State.Finished)
                return true;
            if (state == State.Flushing)
            {
                if (!deflater.IsFinished)
                {
                    read = deflater.Deflate(buffer, offset, count);
                    if (read > 0)
                        return true;

                    Debug.Assert(deflater.IsFinished);
                }
                state = State.Footer;
                return TryReadAvailable(buffer, offset, count, out read);
            }
            if (deflater.IsNeedingInput)
                return false;

            read = deflater.Deflate(buffer, offset, count);

            return read > 0;
        }

        private byte[] BuildHeader()
        {
            var time = (int)((DateTime.Now.Ticks - new DateTime(1970, 1, 1).Ticks) / 10000000L);  // Ticks give back 100ns intervals

            return new byte[]
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
        }

        private byte[] BuildFooter()
        {
            var totalIn = (uint)(deflater.TotalIn & 0xffffffff);
            var crc = (uint)(this.crc.Value & 0xffffffff);

            unchecked
            {
                return new[]
                {
                    (byte)crc, (byte)(crc >> 8),
                    (byte)(crc >> 16), (byte)(crc >> 24),

                    (byte)totalIn, (byte)(totalIn >> 8),
                    (byte)(totalIn >> 16), (byte)(totalIn >> 24)
                };
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException();
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private enum State
        {
            Header,
            Content,
            Flushing,
            Footer,
            Finished
        }
    }
}
