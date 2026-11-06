using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public class SensorManager
    {
        private const ushort PRODUCT_ID = 0x4400;
        private const int S1_W = 1000, S1_H = 1506, S1_PIXELS = S1_W * S1_H, S1_BUFFER = S1_PIXELS * 2 + 16;
        private const int S2_W = 1300, S2_H = 1706, S2_PIXELS = S2_W * S2_H, S2_BUFFER = S2_PIXELS * 2 + 16;
        private const int ALLOC_SIZE = 4_450_000;
        private const int HEADER_SKIP = 14, FOOTER_SKIP = 2;

        private IntPtr _handle = new IntPtr(-1);
        private readonly IntPtr INVALID = new IntPtr(-1);
        private bool _isSize1 = true;
        private ushort[]? _darkPixels;
        private ushort[]? _lastPixels;

        public bool IsConnected => _handle != INVALID;
        public string Serial { get; private set; } = "";
        public byte SensorType { get; private set; }
        public byte Firmware { get; private set; }
        public int Width => _isSize1 ? S1_W : S2_W;
        public int Height => _isSize1 ? S1_H : S2_H;
        public int PixelCount => _isSize1 ? S1_PIXELS : S2_PIXELS;
        public int ExpectedBuffer => _isSize1 ? S1_BUFFER : S2_BUFFER;

        // ═══ CONNECT ═══
        public ConnectResult Connect()
        {
            try
            {
                _handle = CMOS_USB.USB_OpenDevice(PRODUCT_ID);
                if (_handle == INVALID)
                    return ConnectResult.Fail("No sensor detected.\nCheck USB connection and driver.");

                IntPtr pipe = CMOS_USB.USB_OpenPipe(_handle);
                if (pipe == INVALID)
                {
                    Disconnect();
                    return ConnectResult.Fail("USB pipe failed.\nTry a different USB port.");
                }

                CMOS_USB.UnitIntegrationParameter ip = new();
                uint r = CMOS_USB.HPK_GetSensorInformation(_handle, ref ip, out var info);
                if (r != 0)
                {
                    Disconnect();
                    return ConnectResult.Fail($"Sensor info failed (0x{r:X4}).");
                }

                SensorType = info.Sensor_Type;
                Firmware = info.Firmware_Version;
                Serial = info.Sensor_Type.ToString("X2") + info.Lot_Serial_No.ToString("X4");
                _isSize1 = info.Sensor_Type < 0x80;

                var darkResult = AcquireDark();
                if (!darkResult.Success)
                {
                    Disconnect();
                    return ConnectResult.Fail("Dark image failed: " + darkResult.Error);
                }

                return ConnectResult.Ok(Serial, SensorType, Firmware, Width, Height);
            }
            catch (Exception ex)
            {
                Disconnect();
                return ConnectResult.Fail($"Connection error: {ex.Message}");
            }
        }

        // ═══ DARK IMAGE ═══
        private ConnectResult AcquireDark()
        {
            CMOS_USB.UnitXrayImage param;
            param.Mode = 0x03;
            param.IntegParam.Xray_Incident_Threshold = 440;
            param.IntegParam.Xray_Integration_End_Threshold = 437;
            param.IntegParam.Integration_Time = 500;

            byte[] buf = new byte[ALLOC_SIZE];
            uint r = CMOS_USB.HPK_GetXrayImage(_handle, buf, out ulong len, ref param);

            if (r != 0)
                return ConnectResult.Fail($"Dark capture error 0x{r:X4}");

            if ((long)len != ExpectedBuffer)
                return ConnectResult.Fail($"Buffer mismatch: got {len}, expected {ExpectedBuffer}");

            _darkPixels = ExtractPixels(buf);
            return ConnectResult.Ok(Serial, SensorType, Firmware, Width, Height);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CAPTURE X-RAY
        //
        //  DISPLAY CONVERSION: log + invert, windowed on percentiles of the
        //  exposed pixels (see ImageEnhancer.RawToDisplay). Percentiles, not
        //  absolute min/max, so a single dead (0) or blown-out (65535) pixel
        //  can't wreck the contrast — that's what caused the old "too white"
        //  and "too black" captures.
        // ═══════════════════════════════════════════════════════════════
        public CaptureResult Capture(
            byte mode = 0x01,
            ushort startThresh = 440,
            ushort endThresh = 437,
            double integMs = 500,
            int timeoutSeconds = 30)
        {
            if (!IsConnected)
                return CaptureResult.Fail("Not connected.");

            try
            {
                CMOS_USB.USB_ResumeDevice(_handle);

                CMOS_USB.UnitXrayImage param;
                param.Mode = mode;
                param.IntegParam.Xray_Incident_Threshold = startThresh;
                param.IntegParam.Xray_Integration_End_Threshold = endThresh;
                param.IntegParam.Integration_Time = integMs;

                byte[] buf = new byte[ALLOC_SIZE];

                // Timeout timer — aborts capture if no X-ray in 30s
                var captureHandle = _handle;
                Timer? timeoutTimer = null;
                bool timedOut = false;

                timeoutTimer = new Timer(_ =>
                {
                    timedOut = true;
                    try { CMOS_USB.HPK_AbortBulkPipe(captureHandle); }
                    catch { }
                }, null, timeoutSeconds * 1000, Timeout.Infinite);

                // BLOCKS until X-ray detected or timeout
                uint r = CMOS_USB.HPK_GetXrayImage(_handle, buf, out ulong len, ref param);

                timeoutTimer?.Dispose();

                if (timedOut)
                    return CaptureResult.Fail($"Timeout after {timeoutSeconds}s — no X-ray detected.\nCheck X-ray source and sensor position.");

                if (r == 0x3E3)
                    return CaptureResult.Fail("Capture aborted.");

                if (r != 0)
                    return CaptureResult.Fail($"Capture error 0x{r:X4}");

                if ((long)len != ExpectedBuffer)
                    return CaptureResult.Fail($"Buffer: got {len}, expected {ExpectedBuffer}");

                // ─── Signed dark subtraction ───
                // Signed math prevents wrap-around clipping artifacts.
                ushort[] raw = ExtractPixels(buf);
                _lastPixels = new ushort[PixelCount];
                for (int i = 0; i < PixelCount && i < raw.Length; i++)
                {
                    int dark = (_darkPixels != null && i < _darkPixels.Length) ? _darkPixels[i] : 0;
                    int val = raw[i] - dark;
                    _lastPixels[i] = val > 0 ? (ushort)val : (ushort)0;
                }

                // ─── RAW → DISPLAY: log + invert + window (see ImageEnhancer.RawToDisplay) ───
                // Gives the standard radiograph look (air black, teeth white) and stays 16-bit.
                var bmp = ImageEnhancer.RawToDisplayBitmap(_lastPixels, Width, Height, out ushort p99);
                int stride = Width * 2;

                // Flag if no real X-ray data (99th percentile of signal; real X-ray signal >> 100)
                bool isNoiseOnly = p99 < 100;

                // ─── Save raw 16-bit TIFF (preserves original unprocessed data) ───
                try
                {
                    var rawDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "DentalX_Captures", "raw");
                    Directory.CreateDirectory(rawDir);

                    var rawBmp = BitmapSource.Create(
                        Width, Height, 96, 96,
                        PixelFormats.Gray16, null,
                        _lastPixels, stride);

                    string rawPath = Path.Combine(rawDir,
                        $"raw_{DateTime.Now:yyyyMMdd_HHmmss}.tif");

                    using var fs = new FileStream(rawPath, FileMode.Create);
                    var enc = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
                    enc.Frames.Add(BitmapFrame.Create(rawBmp));
                    enc.Save(fs);
                }
                catch { /* raw save is optional, don't fail capture */ }

                var result = CaptureResult.Ok(bmp, _lastPixels, Width, Height);
                result.IsNoiseOnly = isNoiseOnly;
                return result;
            }
            catch (Exception ex)
            {
                return CaptureResult.Fail($"Capture exception: {ex.Message}");
            }
        }

        // ═══ ABORT ═══
        public void Abort()
        {
            if (IsConnected)
            {
                try { CMOS_USB.HPK_AbortBulkPipe(_handle); }
                catch { }
            }
        }

        // ═══ DISCONNECT ═══
        public void Disconnect()
        {
            if (_handle != INVALID)
            {
                try { CMOS_USB.HPK_AbortBulkPipe(_handle); } catch { }
                try { CMOS_USB.USB_SuspendDevice(_handle, 1); } catch { }
                try { CMOS_USB.USB_CloseDevice(_handle); } catch { }
                _handle = INVALID;
            }
            Serial = "";
            _darkPixels = null;
            _lastPixels = null;
        }

        // ═══ PIXEL EXTRACTION ═══
        private ushort[] ExtractPixels(byte[] buffer)
        {
            ushort[] pixels = new ushort[PixelCount];
            int j = 0;
            int limit = ExpectedBuffer - FOOTER_SKIP;
            for (int i = HEADER_SKIP; i < limit && j < PixelCount; i += 2)
                pixels[j++] = BitConverter.ToUInt16(buffer, i);
            return pixels;
        }

        public ushort[]? GetLastPixels() => _lastPixels;
    }

    public class ConnectResult
    {
        public bool Success;
        public string Error = "";
        public string Serial = "";
        public byte SensorType;
        public byte Firmware;
        public int Width, Height;

        public static ConnectResult Ok(string s, byte t, byte f, int w, int h)
            => new() { Success = true, Serial = s, SensorType = t, Firmware = f, Width = w, Height = h };
        public static ConnectResult Fail(string e)
            => new() { Success = false, Error = e };
    }

    public class CaptureResult
    {
        public bool Success;
        public string Error = "";
        public BitmapSource? Image;
        public ushort[]? Pixels;
        public int Width, Height;
        public bool IsNoiseOnly;

        public static CaptureResult Ok(BitmapSource img, ushort[] px, int w, int h)
            => new() { Success = true, Image = img, Pixels = px, Width = w, Height = h };
        public static CaptureResult Fail(string e)
            => new() { Success = false, Error = e };
    }
}