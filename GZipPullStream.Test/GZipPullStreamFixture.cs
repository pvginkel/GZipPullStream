using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using NUnit.Framework;

namespace GZipPullStream.Test
{
    [TestFixture]
    public class GZipPullStreamFixture
    {
        static GZipPullStreamFixture()
        {
            BreakingTraceListener.Setup();
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        [TestCase(50000)]
        [TestCase(100000)]
        public void GZipFixedInput(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            AssertInput(input, true, p => new GZipPullStream(p), p => new GZipOutputStream(p), p => new GZipStream(p, CompressionMode.Decompress));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        [TestCase(50000)]
        [TestCase(100000)]
        public void DeflaterFixedInput(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            AssertInput(input, false, p => new DeflatePullStream(p), p => new DeflaterOutputStream(p, new Deflater(Deflater.DEFAULT_COMPRESSION, true)), p => new DeflateStream(p, CompressionMode.Decompress));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        [TestCase(50000)]
        [TestCase(100000)]
        public Task GZipFixedInputAsync(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            return AssertInputAsync(input, true, p => new GZipPullStream(p), p => new GZipOutputStream(p), p => new GZipStream(p, CompressionMode.Decompress));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        [TestCase(50000)]
        [TestCase(100000)]
        public Task DeflaterFixedInputAsync(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            return AssertInputAsync(input, false, p => new DeflatePullStream(p), p => new DeflaterOutputStream(p, new Deflater(Deflater.DEFAULT_COMPRESSION, true)), p => new DeflateStream(p, CompressionMode.Decompress));
        }

        private void AssertInput(byte[] expected, bool clearTimestamp, Func<Stream, Stream> createPullStream, Func<Stream, Stream> createDeflaterStream, Func<Stream, Stream> createInflateStream)
        {
            // Compress using the pull stream.

            byte[] compressed;

            using (var target = new MemoryStream())
            {
                using (var source = createPullStream(new MemoryStream(expected)))
                {
                    source.CopyTo(target);
                }

                compressed = target.ToArray();
            }

            Validate(expected, clearTimestamp, compressed, createDeflaterStream, createInflateStream);
        }

        private async Task AssertInputAsync(byte[] expected, bool clearTimestamp, Func<Stream, Stream> createPullStream, Func<Stream, Stream> createDeflaterStream, Func<Stream, Stream> createInflateStream)
        {
            // Write the input to a file so we have some real async stuff.

            string tmpSource = Path.GetTempFileName();
            string tmpCompressed = Path.GetTempFileName();

            try
            {
                File.WriteAllBytes(tmpSource, expected);

                // Compress using the pull stream.

                using (var target = File.Create(tmpCompressed))
                using (var source = createPullStream(File.OpenRead(tmpSource)))
                {
                    await source.CopyToAsync(target);
                }

                var compressed = File.ReadAllBytes(tmpCompressed);

                Validate(expected, clearTimestamp, compressed, createDeflaterStream, createInflateStream);
            }
            finally
            {
                File.Delete(tmpSource);
                File.Delete(tmpCompressed);
            }
        }

        private void Validate(byte[] expected, bool clearTimestamp, byte[] compressed, Func<Stream, Stream> createDeflaterStream, Func<Stream, Stream> createInflateStream)
        {
            // Compress again using the SharpZipLib writer.

            byte[] expectedCompressed;

            using (var source = new MemoryStream(expected))
            using (var target = new MemoryStream())
            {
                using (var gzTarget = createDeflaterStream(target))
                {
                    source.CopyTo(gzTarget);
                }

                expectedCompressed = target.ToArray();
            }

            if (clearTimestamp)
            {
                // Byte 4-8 in the compressed output is a timestamp. We need to clear
                // this because this is not deterministic.

                ClearDate(compressed);
                ClearDate(expectedCompressed);
            }

            Assert.AreEqual(expectedCompressed, compressed);

            // Decompress using the BCL GZipStream.

            byte[] actual;

            using (var source = createInflateStream(new MemoryStream(compressed)))
            using (var target = new MemoryStream())
            {
                source.CopyTo(target);

                actual = target.ToArray();
            }

            Assert.AreEqual(expected, actual);
        }

        private void ClearDate(byte[] compressed)
        {
            for (int i = 4; i < 8; i++)
            {
                compressed[i] = 0;
            }
        }
    }
}
