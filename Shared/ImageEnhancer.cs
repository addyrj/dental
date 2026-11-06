using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public class ImageEnhancer
    {
        // ═══════════════════════════════════════════════════
        //  RAW SENSOR → DISPLAY (log + invert + window)
        //
        //  The sensor's dark-subtracted signal is LINEAR in X-ray dose: air gets the most dose
        //  (highest value) and enamel/metal the least. Shown as-is that's a flat, washed-out
        //  negative. X-ray attenuation is exponential (Beer–Lambert), so log(signal) is
        //  proportional to tissue thickness/density — that's what spreads enamel, dentin,
        //  bone and pulp apart. Inverting then gives the standard radiograph look: air black,
        //  teeth and fillings white.
        //
        //  The window is taken from EXPOSED pixels only (signal above 20% of the 99th percentile),
        //  so large unexposed areas (sensor corners, cone-cut) don't waste grey levels;
        //  they simply clamp to white, which is how they appear on film too. Measured on real
        //  captures: tooth/bone/root sit at ~30–60% of p99, cone-cut at ~7% — a 2% cutoff let
        //  cone-cut pull the window and left the teeth dark grey.
        // ═══════════════════════════════════════════════════
        public static ushort[] RawToDisplay(ushort[] raw, out ushort p99)
        {
            var hist = new int[65536];
            foreach (var v in raw) hist[v]++;

            p99 = Percentile(hist, raw.Length, 0.99, 0);
            int floor = Math.Max(1, p99 / 5);
            int exposed = 0;
            for (int v = floor; v < 65536; v++) exposed += hist[v];
            exposed = Math.Max(exposed, 1);

            // Window = 0.5% – 99.5% of exposed pixels, in log space. log is monotonic, so the
            // percentile of log(signal) is just log of the signal's percentile.
            double lo = Math.Log(Math.Max(Percentile(hist, exposed, 0.005, floor), (ushort)1));
            double hi = Math.Log(Math.Max(Percentile(hist, exposed, 0.995, floor), (ushort)1));
            if (hi - lo < 0.05) hi = lo + 0.05; // noise-only frame: don't blow up the noise

            // Lookup table over every possible 16-bit value — one log per level, not per pixel
            var lut = new ushort[65536];
            for (int v = 0; v < 65536; v++)
            {
                double t = (Math.Log(Math.Max(v, 1)) - lo) / (hi - lo);
                lut[v] = (ushort)(Math.Clamp(1.0 - t, 0.0, 1.0) * 65535); // 1 - t = invert
            }

            var display = new ushort[raw.Length];
            for (int i = 0; i < raw.Length; i++) display[i] = lut[raw[i]];
            return display;
        }

        // Value at fraction q of the 'count' pixels at or above 'start' in a 16-bit histogram
        private static ushort Percentile(int[] hist, int count, double q, int start)
        {
            long target = (long)(count * q), c = 0;
            for (int v = start; v < 65536; v++)
            {
                c += hist[v];
                if (c > target) return (ushort)v;
            }
            return 65535;
        }

        public static BitmapSource RawToDisplayBitmap(ushort[] raw, int w, int h, out ushort p99)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray16, null, RawToDisplay(raw, out p99), w * 2);
            bmp.Freeze();
            return bmp;
        }

        // ─── WPF BitmapSource → OpenCV Mat (GRAYSCALE) ───
        // 16-bit sources (sensor captures) stay 16-bit (CV_16UC1) so enhancement works on the
        // sensor's full grey-level range; squashing to 8-bit first threw most of it away.
        // Anything else is converted to Gray8 (CV_8UC1) as before.
        // Uses Marshal.Copy (reliable) instead of SetArray (buggy)
        public static Mat BitmapSourceToMat(BitmapSource source)
        {
            int w = source.PixelWidth;
            int h = source.PixelHeight;

            if (source.Format == PixelFormats.Gray16)
            {
                byte[] bytes16 = new byte[h * w * 2];
                source.CopyPixels(bytes16, w * 2, 0);
                var mat16 = new Mat(h, w, MatType.CV_16UC1);
                Marshal.Copy(bytes16, 0, mat16.Data, bytes16.Length);
                return mat16;
            }

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
            if (mat.Type() == MatType.CV_16UC1)
            {
                int w = mat.Width, h = mat.Height;
                byte[] bytes16 = new byte[h * w * 2];
                Marshal.Copy(mat.Data, bytes16, 0, bytes16.Length);
                var bmp16 = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray16, null, bytes16, w * 2);
                bmp16.Freeze();
                return bmp16;
            }

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
        // sigmaColor is in 8-bit grey levels (0–255) whatever the image depth.
        public static Mat ApplyDenoise(Mat input, int d = 7, double sigmaColor = 75, double sigmaSpace = 75)
        {
            using var gray = EnsureGray(input);
            return Bilateral(gray, d, sigmaColor, sigmaSpace);
        }

        // BilateralFilter only accepts 8-bit or float, so 16-bit images go through float (0–1).
        private static Mat Bilateral(Mat gray, int d, double sigmaColor, double sigmaSpace)
        {
            var result = new Mat();
            if (gray.Type() != MatType.CV_16UC1)
            {
                Cv2.BilateralFilter(gray, result, d, sigmaColor, sigmaSpace);
                return result;
            }
            using var f = new Mat();
            using var fOut = new Mat();
            gray.ConvertTo(f, MatType.CV_32F, 1.0 / 65535);
            Cv2.BilateralFilter(f, fOut, d, sigmaColor / 255.0, sigmaSpace);
            fOut.ConvertTo(result, MatType.CV_16U, 65535);
            return result;
        }

        // Gamma for either depth: 8-bit via a 256-entry LUT, 16-bit via float pow
        // (Cv2.LUT only takes 8-bit input).
        private static Mat Gamma(Mat gray, double gamma)
        {
            var result = new Mat();
            if (gray.Type() == MatType.CV_16UC1)
            {
                using var f = new Mat();
                gray.ConvertTo(f, MatType.CV_32F, 1.0 / 65535);
                Cv2.Pow(f, 1.0 / gamma, f);
                f.ConvertTo(result, MatType.CV_16U, 65535);
                return result;
            }
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Clamp(Math.Pow(i / 255.0, 1.0 / gamma) * 255, 0, 255);
            using var lutMat = new Mat(1, 256, MatType.CV_8UC1);
            Marshal.Copy(lut, 0, lutMat.Data, 256);
            Cv2.LUT(gray, lutMat, result);
            return result;
        }

        // Brightness offset in 8-bit grey levels, scaled for 16-bit images
        public static Mat ApplyBrightness(Mat input, double offset8)
        {
            var result = new Mat();
            input.ConvertTo(result, -1, 1.0, input.Type() == MatType.CV_16UC1 ? offset8 * 257 : offset8);
            return result;
        }

        // 8-bit copy for display-only operations (colour maps) that OpenCV only supports on 8-bit
        public static Mat To8Bit(Mat input)
        {
            var result = new Mat();
            if (input.Type() == MatType.CV_16UC1) input.ConvertTo(result, MatType.CV_8U, 1.0 / 257);
            else input.CopyTo(result);
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
            using var lap = new Mat();
            using var edges = new Mat();
            var result = new Mat();
            Cv2.Laplacian(gray, lap, MatType.CV_32F, ksize: 3);
            lap.ConvertTo(edges, gray.Type()); // negatives clip to 0, same as before
            Cv2.AddWeighted(gray, 1.0, edges, strength, 0, result);
            return result;
        }

        // ═══════════════════════════════════════════════════
        //  5. GAMMA CORRECTION
        // ═══════════════════════════════════════════════════
        public static Mat ApplyGamma(Mat input, double gamma = 0.7)
        {
            using var gray = EnsureGray(input);
            return Gamma(gray, gamma);
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
            using var gray8 = To8Bit(gray); // ApplyColorMap is 8-bit only
            var result = new Mat();
            Cv2.ApplyColorMap(gray8, result, colormap);
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
                var denoised = Bilateral(current, 7, 75, 75);
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
            var gammaResult = Gamma(current, gamma);
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