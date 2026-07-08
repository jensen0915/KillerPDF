using System.Windows;
using Xunit;

namespace KillerPDF.Tests
{
    public sealed class TextAnnotationRasterizerTests
    {
        [Theory]
        [InlineData("中文測試 ABC 123")]
        [InlineData("日本語テスト")]
        [InlineData("한국어 테스트")]
        [InlineData("全形：，。")]
        public void ContainsCjkDetectsCjkText(string text)
        {
            Assert.True(TextAnnotationRasterizer.ContainsCjk(text));
        }

        [Fact]
        public void ContainsCjkIgnoresPlainEnglish()
        {
            Assert.False(TextAnnotationRasterizer.ContainsCjk("This is an English test. ABC 123"));
        }

        [Fact]
        public void RenderToPngProducesPngBytes()
        {
            var annotation = new TextAnnotation
            {
                Content = "中文測試 ABC 123",
                Position = new Point(0, 0),
                Width = 200,
                Height = 40,
                FontSize = 14
            };

            var png = TextAnnotationRasterizer.RenderToPng(annotation);

            Assert.Equal(0x89, png[0]);
            Assert.Equal(0x50, png[1]);
            Assert.Equal(0x4E, png[2]);
            Assert.Equal(0x47, png[3]);
        }
    }
}
