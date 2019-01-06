using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
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
        public void FixedInput(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            AssertInput(input);
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
        public Task FixedInputAsync(int length)
        {
            var input = new byte[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = (byte)(i & 0xff);
            }

            return AssertInputAsync(input);
        }

        private void AssertInput(byte[] expected)
        {
            // Compress using the pull stream.

            byte[] compressed;

            using (var target = new MemoryStream())
            {
                using (var source = new GZipPullStream(new MemoryStream(expected)))
                {
                    source.CopyTo(target);
                }

                compressed = target.ToArray();
            }

            Validate(expected, compressed);
        }

        private async Task AssertInputAsync(byte[] expected)
        {
            // Write the input to a file so we have some real async stuff.

            string tmpSource = Path.GetTempFileName();
            string tmpCompressed = Path.GetTempFileName();

            try
            {
                File.WriteAllBytes(tmpSource, expected);

                // Compress using the pull stream.

                using (var target = File.Create(tmpCompressed))
                using (var source = new GZipPullStream(File.OpenRead(tmpSource)))
                {
                    await source.CopyToAsync(target);
                }

                var compressed = File.ReadAllBytes(tmpCompressed);

                Validate(expected, compressed);
            }
            finally
            {
                File.Delete(tmpSource);
                File.Delete(tmpCompressed);
            }
        }

        private void Validate(byte[] expected, byte[] compressed)
        {
            // Compress again using the SharpZipLib writer.

            byte[] expectedCompressed;

            using (var source = new MemoryStream(expected))
            using (var target = new MemoryStream())
            {
                using (var gzTarget = new GZipOutputStream(target))
                {
                    source.CopyTo(gzTarget);
                }

                expectedCompressed = target.ToArray();
            }

            // Byte 4-8 in the compressed output is a timestamp. We need to clear
            // this because this is not deterministic.

            ClearDate(compressed);
            ClearDate(expectedCompressed);

            Assert.AreEqual(expectedCompressed, compressed);
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
