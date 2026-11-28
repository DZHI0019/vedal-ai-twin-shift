using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TwinShift
{
    internal static class SceneArtwork
    {
        static readonly byte[] Bayer = {
             0, 48, 12, 60,  3, 51, 15, 63,
            32, 16, 44, 28, 35, 19, 47, 31,
             8, 56,  4, 52, 11, 59,  7, 55,
            40, 24, 36, 20, 43, 27, 39, 23,
             2, 50, 14, 62,  1, 49, 13, 61,
            34, 18, 46, 30, 33, 17, 45, 29,
            10, 58,  6, 54,  9, 57,  5, 53,
            42, 26, 38, 22, 41, 25, 37, 21
        };

        internal static BitmapSource Load(bool night)
        {
            return Feather(Files.Image(night ? "evo.jpg" : "neo.jpg"), night);
        }

        internal static BitmapSource Feather(BitmapSource source, bool night)
        {
            var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
            var pixels = new byte[height * stride];
            image.CopyPixels(pixels, stride, 0);
            var horizontal = new double[width];
            for (int x = 0; x < width; x++)
            {
                double u = (double)x / (width - 1);
                horizontal[x] = .09 * Smooth(u / .50) + .50 * Smooth((u - .30) / .60);
            }
            double strength = night ? 1 : .79;
            for (int y = 0; y < height; y++)
            {
                double v = (double)y / (height - 1);
                double vertical = strength * Smooth(v / .19) * Smooth((1 - v) / .25);
                for (int x = 0, pixel = y * stride; x < width; x++, pixel += 4)
                {
                    double alpha = horizontal[x] * vertical;
                    // Dither the premultiplied channels as well as alpha to retain detail near black.
                    double threshold = (Bayer[(y & 7) * 8 + (x & 7)] + .5) / 64;
                    pixels[pixel] = (byte)(pixels[pixel] * alpha + threshold);
                    pixels[pixel + 1] = (byte)(pixels[pixel + 1] * alpha + threshold);
                    pixels[pixel + 2] = (byte)(pixels[pixel + 2] * alpha + threshold);
                    pixels[pixel + 3] = (byte)(255 * alpha + threshold);
                }
            }
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        static double Smooth(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }
    }
}
