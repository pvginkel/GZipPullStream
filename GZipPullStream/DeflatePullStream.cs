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

namespace GZipPullStream
{
    public class DeflatePullStream : Stream
    {
        public const int DefaultBufferSize = 4096;

        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        protected readonly Deflater _deflater;
        protected readonly byte[] _buffer;
        private ArraySegment<byte> _available;
        private State _state;

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

        public DeflatePullStream(Stream stream)
            : this(stream, false)
        {
        }

        public DeflatePullStream(Stream stream, bool leaveOpen)
            : this(stream, leaveOpen, null, DefaultBufferSize)
        {
        }

        public DeflatePullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize)
            : this(stream, leaveOpen, bufferSize, new Deflater(GetCompressionLevel(compressionLevel), true))
        {
        }

        public DeflatePullStream(Stream stream, bool leaveOpen, CompressionLevel? compressionLevel, int bufferSize, bool includeHeader)
            : this(stream, leaveOpen, bufferSize, new Deflater(GetCompressionLevel(compressionLevel), !includeHeader))
        {
        }

        protected DeflatePullStream(Stream stream, bool leaveOpen, int bufferSize, Deflater deflater)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
            _buffer = new byte[bufferSize];
            _deflater = deflater;
            _state = State.Header;
        }

        protected static int GetCompressionLevel(CompressionLevel? compressionLevel)
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
            if (!_leaveOpen)
                _stream.Close();

            base.Close();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (TryReadAvailable(buffer, offset, count, out int read))
                    return read;

                UpdateInput(await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken));
            }
        }

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
