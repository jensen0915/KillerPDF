using System;
using System.IO;
using System.Linq;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests
{
    public class OfficeWorkflowTests
    {
        [Theory]
        [InlineData("1-3,5")]
        [InlineData("１－３，５")]
        [InlineData("1-3、5")]
        [InlineData(" 1 - 3, 5, 3 ")]
        public void PrintRangeAcceptsTraditionalChineseInput(string value)
            => Assert.Equal(new[] { 0, 1, 2, 4 }, PrintPageRange.Parse(value, 5));

        [Theory]
        [InlineData("0")]
        [InlineData("6")]
        [InlineData("1,abc")]
        [InlineData("3-1")]
        [InlineData("1-999999999")]
        [InlineData("1,")]
        [InlineData("1--3")]
        [InlineData("-1")]
        public void InvalidRangeNeverFallsBackToAllPages(string value)
            => Assert.Empty(PrintPageRange.Parse(value, 5));

        [Fact]
        public void BlankRangeMeansAllPages()
            => Assert.Equal(Enumerable.Range(0, 5), PrintPageRange.Parse(" ", 5));

        [Fact]
        public void AtomicCopyPreservesLockedDestinationAndCleansStaging()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerPDF-atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "source.pdf"), target = Path.Combine(folder, "target.pdf");
            try
            {
                File.WriteAllText(source, "new"); File.WriteAllText(target, "original");
                using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.ThrowsAny<IOException>(() => AtomicFile.Copy(source, target));
                Assert.Equal("original", File.ReadAllText(target));
                Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
                AtomicFile.Copy(source, target);
                Assert.Equal("new", File.ReadAllText(target));
            }
            finally
            {
                foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ChineseFormAppearanceProducesPng(bool multiline)
        {
            var bytes = TextAnnotationRasterizer.RenderFormFieldToPng("陳怡君  臺北市政府\n地址：臺灣", 180, 40,
                12, multiline, System.Windows.TextAlignment.Center);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, bytes.Take(4));
        }
    }
}
