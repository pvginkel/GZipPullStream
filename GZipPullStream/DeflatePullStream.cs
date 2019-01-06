using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip.Compression;

#pragma warning disable 1591

namespace GZipPullStream
{
    /// <summary>
    /// <see cref="DeflateStream"/> implementation that allows pulling compressed
    /// data from it, instead of it writing compressed data to the base stream.
    /// </summary>
    public class DeflatePullStream : Stream
    {
        /// <summary>
        /// Default buffer size if none is provided.
        /// </summary>
        public const int DefaultBufferSize = 4096;

        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        protected readonly Deflater _deflater;
        protected readonly byte[] _buffer;
        private ArraySegment<byte> _available;
        private State _state;

        /// <inheritdoc/>
        public override bool CanRead => true;
        /// <inheritdoc/>
        public override bool CanSeek => false;
        /// <inheritdoc/>
        public override bool CanTimeout => false;
        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Create a new <see cref="DeflatePullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <remarks>
        /// By default, no ZLib header is included for compatibility with <see cref="DeflateStream"/>.
        /// </remarks>
        public DeflatePullStream(Stream stream)
            : this(stream, false)
        {
        }

        /// <summary>
        /// Create a new <see cref="DeflatePullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        /// <remarks>
        /// By default, no ZLib header is included for compatibility with <see cref="DeflateStream"/>.
        /// </remarks>
        public DeflatePullStream(Stream stream, bool leaveOpen)
            : this(stream, leaveOpen, null, DefaultBufferSize)
        {
        }

        /// <summary>
        /// Create a new <see cref="DeflatePullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        /// <param name="compressionLevel">Compression level.</param>
        /// <param name="bufferSize">Buffer size.</param>
        /// <remarks>
        /// By default, no ZLib header is included for compatibility with <see cref="DeflateStream"/>.
        /// </remarks>
        public DeflatePullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize)
            : this(stream, leaveOpen, bufferSize, new Deflater(GetCompressionLevel(compressionLevel), true))
        {
        }

        /// <summary>
        /// Create a new <see cref="DeflatePullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        /// <param name="compressionLevel">Compression level.</param>
        /// <param name="bufferSize">Buffer size.</param>
        /// <param name="includeHeader">Whether to include a ZLib header; defaults to false.</param>
        /// <remarks>
        /// By default, no ZLib header is included for compatibility with <see cref="DeflateStream"/>.
        /// </remarks>
        public DeflatePullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize, bool includeHeader)
            : this(stream, leaveOpen, bufferSize, new Deflater(GetCompressionLevel(compressionLevel), !includeHeader))
        {
        }

        /// <summary>
        /// Create a new <see cref="DeflatePullStream"/> instance.
        /// </summary>
        /// <param name="stream">Stream to read uncompressed data from.</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when closing this stream.</param>
        /// <param name="bufferSize">Buffer size.</param>
        /// <param name="deflater"><see cref="Deflater"/> instance used for compression.</param>
        protected DeflatePullStream(Stream stream, bool leaveOpen, int bufferSize, Deflater deflater)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
            _buffer = new byte[bufferSize];
            _deflater = deflater;
            _state = State.Header;
        }

        internal static int GetCompressionLevel(CompressionLevel? compressionLevel)
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

        /// <inheritdoc/>
        public override void Close()
        {
            if (!_leaveOpen)
                _stream.Close();

            base.Close();
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (TryReadAvailable(buffer, offset, count, out int read))
                    return read;

                UpdateInput(await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken));
            }
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                if (TryReadAvailable(buffer, offset, count, out int read))
                    return read;

                UpdateInput(_stream.Read(_buffer, 0, _buffer.Length));
            }
        }

        protected virtual void UpdateInput(int read)
        {
            if (read == 0)
            {
                _state = State.Flushing;
                _deflater.Finish();
            }
            else
            {
                _deflater.SetInput(_buffer, 0, read);
            }
        }

        private bool TryReadAvailable(byte[] buffer, int offset, int count, out int read)
        {
            if (_available.Count > 0)
            {
                read = Math.Min(_available.Count, count);
                Array.Copy(_available.Array, _available.Offset, buffer, offset, read);
                _available = new ArraySegment<byte>(_available.Array, _available.Offset + read, _available.Count - read);
                return true;
            }

            if (_state == State.Header)
            {
                _state = State.Content;
                _available = BuildHeader();
                return TryReadAvailable(buffer, offset, count, out read);
            }
            if (_state == State.Footer)
            {
                _state = State.Finished;
                _available = BuildFooter();
                return TryReadAvailable(buffer, offset, count, out read);
            }

            read = 0;

            if (_state == State.Finished)
                return true;
            if (_state == State.Flushing)
            {
                if (!_deflater.IsFinished)
                {
                    read = _deflater.Deflate(buffer, offset, count);
                    if (read > 0)
                        return true;

                    Debug.Assert(_deflater.IsFinished);
                }
                _state = State.Footer;
                return TryReadAvailable(buffer, offset, count, out read);
            }
            if (_deflater.IsNeedingInput)
                return false;

            read = _deflater.Deflate(buffer, offset, count);

            return read > 0;
        }

        protected virtual ArraySegment<byte> BuildHeader()
        {
            return new ArraySegment<byte>();
        }

        protected virtual ArraySegment<byte> BuildFooter()
        {
            return new ArraySegment<byte>();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override int EndRead(IAsyncResult asyncResult)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
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
