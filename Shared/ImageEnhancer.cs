using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public class ImageEnhancer
    {
        // ─── WPF BitmapSource → OpenCV Mat (GRAYSCALE) ───
        // Dental X-rays are grayscale — convert directly to Gray8
        // Uses Marshal.Copy (reliable) instead of SetArray (buggy)
        public static Mat BitmapSourceToMat(BitmapSource source)
        {
            int w = source.PixelWidth;
            int h = source.PixelHeight;

            // Convert to Gray8 in WPF — avoids all BGRA channel issues
            var gray8 = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            int stride = w;
            byte[] pixels = new byte[h * stride];
            gray8.CopyPixels(pixels, stride, 0);

            // Create 1-channel Mat using Marshal.Copy (not SetArray)
            var mat = new Mat(h, w, MatType.CV_8UC1);
            Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
            return mat;
        }

        // ─── OpenCV Mat → WPF BitmapSource ───
        // Uses Marshal.Copy (reliable) instead of GetArray (buggy)
        public static BitmapSource MatToBitmapSource(Mat mat)
        {
            if (mat.Channels() == 1)
            {
                // Grayscale → Gray8 BitmapSource
                int w = mat.Width, h = mat.Height;
                int stride = w;
                byte[] pixels = new byte[h * stride];
                Marshal.Copy(mat.Data, pixels, 0, pixels.Length);

                var bmp = BitmapSource.Create(w, h, 96, 96,
                    PixelFormats.Gray8, null, pixels, stride);
                bmp.Freeze();
                return bmp;
            }
            else if (mat.Channels() == 3)
            {
                // BGR → Bgr24 BitmapSource
                int w = mat.Width, h = mat.Height;
                int stride = w * 3;
                byte[] pixels = new byte[h * stride];
                Marshal.Copy(mat.Data, pixels, 0, pixels.Length);

                var bmp = BitmapSource.Create(w, h, 96, 96,
                    PixelFormats.Bgr24, null, pixels, stride);
                bmp.Freeze();
                return bmp;
            }
            else
            {
                // 4-channel → convert to BGR first
                using var bgr = new Mat();
                Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
                return MatToBitmapSource(bgr);
            }
        }

        // ─── Ensure grayscale (input is already gray from BitmapSourceToMat) ───
        private static Mat EnsureGray(Mat input)
        {
            if (input.Channels() == 1) return input.Clone();
            if (input.Channels() == 4)
            {
                var g = new Mat();
                Cv2.CvtColor(input, g, ColorConversionCodes.BGRA2GRAY);
                return g;
            }
            var gray = new Mat();
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        // ═══════════════════════════════════════════════════
        //  1. CLAHE
        // ═══════════════════════════════════════════════════
        public static Mat ApplyCLAHE(Mat input, double clipLimit = 3.0, int tileSize = 8)
        {
            using var gray = EnsureGray(input);
            var result = new Mat();
            using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(tileSize, tileSize));
            clahe.Apply(gray, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  2. DENOISE (bilateral filter)
        // ═══════════════════════════════════════════════════
        public static Mat ApplyDenoise(Mat input, int d = 7, double sigmaColor = 75, double sigmaSpace = 75)
        {
            using var gray = EnsureGray(input);
            var result = new Mat();
            Cv2.BilateralFilter(gray, result, d, sigmaColor, sigmaSpace);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  3. SHARPEN (unsharp masking)
        // ═══════════════════════════════════════════════════
        public static Mat ApplySharpen(Mat input, double sigma = 1.5, double strength = 1.0)
        {
            using var gray = EnsureGray(input);
            using var blurred = new Mat();
            var result = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(0, 0), sigma);
            Cv2.AddWeighted(gray, 1.0 + strength, blurred, -strength, 0, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  4. EDGE ENHANCEMENT
        // ═══════════════════════════════════════════════════
        public static Mat ApplyEdgeEnhance(Mat input, double strength = 0.3)
        {
            using var gray = EnsureGray(input);
            using var edges16 = new Mat();
            using var edges = new Mat();
            var result = new Mat();
            Cv2.Laplacian(gray, edges16, MatType.CV_16S, ksize: 3);
            edges16.ConvertTo(edges, MatType.CV_8U);
            Cv2.AddWeighted(gray, 1.0, edges, strength, 0, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  5. GAMMA CORRECTION
        // ═══════════════════════════════════════════════════
        public static Mat ApplyGamma(Mat input, double gamma = 0.7)
        {
            using var gray = EnsureGray(input);
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Clamp(Math.Pow(i / 255.0, 1.0 / gamma) * 255, 0, 255);

            using var lutMat = new Mat(1, 256, MatType.CV_8UC1);
            Marshal.Copy(lut, 0, lutMat.Data, 256);

            var result = new Mat();
            Cv2.LUT(gray, lutMat, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  6. INVERT
        // ═══════════════════════════════════════════════════
        public static Mat ApplyInvert(Mat input)
        {
            using var gray = EnsureGray(input);
            var result = new Mat();
            Cv2.BitwiseNot(gray, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  7. COLOR MAP
        // ═══════════════════════════════════════════════════
        public static Mat ApplyColorMap(Mat input, ColormapTypes colormap)
        {
            using var gray = EnsureGray(input);
            var result = new Mat();
            Cv2.ApplyColorMap(gray, result, colormap);
            return result; // Returns 3-channel BGR
        }

        // ═══════════════════════════════════════════════════
        //  8. FULL ENHANCEMENT PIPELINE
        // ═══════════════════════════════════════════════════
        public static Mat FullEnhance(Mat input,
            double claheClip = 3.0,
            double sharpStrength = 0.8,
            double gamma = 0.75,
            bool denoise = true)
        {
            Mat current = EnsureGray(input);

            // Step 1: Denoise
            if (denoise)
            {
                var denoised = new Mat();
                Cv2.BilateralFilter(current, denoised, 7, 75, 75);
                current.Dispose();
                current = denoised;
            }

            // Step 2: CLAHE
            var claheResult = new Mat();
            using (var clahe = Cv2.CreateCLAHE(claheClip, new Size(8, 8)))
            {
                clahe.Apply(current, claheResult);
            }
            current.Dispose();
            current = claheResult;

            // Step 3: Sharpen
            using var blurred = new Mat();
            var sharpened = new Mat();
            Cv2.GaussianBlur(current, blurred, new Size(0, 0), 1.5);
            Cv2.AddWeighted(current, 1.0 + sharpStrength, blurred, -sharpStrength, 0, sharpened);
            current.Dispose();
            current = sharpened;

            // Step 4: Gamma
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Clamp(Math.Pow(i / 255.0, 1.0 / gamma) * 255, 0, 255);

            using var lutMat = new Mat(1, 256, MatType.CV_8UC1);
            Marshal.Copy(lut, 0, lutMat.Data, 256);

            var gammaResult = new Mat();
            Cv2.LUT(current, lutMat, gammaResult);
            current.Dispose();

            return gammaResult;
        }

        // ─── Presets ───
        public static Mat PresetNormal(Mat input)
            => FullEnhance(input, claheClip: 2.5, sharpStrength: 0.6, gamma: 0.8);

        public static Mat PresetHighContrast(Mat input)
            => FullEnhance(input, claheClip: 4.0, sharpStrength: 1.0, gamma: 0.65);

        public static Mat PresetBone(Mat input)
            => FullEnhance(input, claheClip: 3.5, sharpStrength: 1.2, gamma: 0.7);

        public static Mat PresetEndodontic(Mat input)
            => FullEnhance(input, claheClip: 3.0, sharpStrength: 1.5, gamma: 0.6);

        public static Mat PresetPeriodontal(Mat input)
            => FullEnhance(input, claheClip: 3.5, sharpStrength: 0.8, gamma: 0.75);
    }
}