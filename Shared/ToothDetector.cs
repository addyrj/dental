using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DentalX
{
    // One auto-detected tooth: a length line (crown tip → root apex, along the tooth's long axis)
    // and a breadth line (across the crown at its widest point, perpendicular to that axis).
    // All points are in FULL-RESOLUTION image-pixel coordinates.
    public class DetectedTooth
    {
        public Point2d Crown, Apex;          // length line
        public Point2d WidthA, WidthB;       // breadth line
        public double LengthPx => Dist(Crown, Apex);
        public double WidthPx => Dist(WidthA, WidthB);
        private static double Dist(Point2d a, Point2d b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    // Classical (non-AI) tooth finder for periapical X-rays, built on how they're laid out:
    // teeth roughly parallel, crowns on the side facing air, roots running into bone.
    //
    //  1. ORIENT: the dominant edge direction inside tissue (root sides, PDL lines) gives the
    //     teeth's axis. Rotate so teeth are vertical and flip so the air side is at the top
    //     — every later step then works in plain rows and columns.
    //  2. TEETH ACROSS: in the band just below the air boundary (the crowns), tooth columns
    //     are bright and the gaps between teeth are dark. Bright runs = teeth; each run's
    //     width at its widest row is the crown BREADTH.
    //  3. CROWN TIP: where air meets enamel at the tooth's centre.
    //  4. APEX: walk down the root, following its centre, while it stays brighter than the
    //     strips either side (PDL + bone); the apex is where that contrast fades for good.
    //
    // Teeth are expected bright (radiograph convention); older captures were stored as
    // negatives, so both polarities are tried and the stronger result wins.
    //
    // It is an ESTIMATE — a faint apex stops the length short, overlapping teeth merge, and
    // molars are measured along one axis — which is why results are drawn as draggable lines.
    public static class ToothDetector
    {
        private const int WorkHeight = 600;

        public static List<DetectedTooth> Detect(Mat input)
        {
            using var gray8 = ToGray8(input);
            double scale = (double)WorkHeight / gray8.Height;
            using var small = new Mat();
            Cv2.Resize(gray8, small, new Size(Math.Max(1, (int)(gray8.Width * scale)), WorkHeight), 0, 0, InterpolationFlags.Area);

            var normal = DetectWorking(small, invert: false, out double scoreN);
            var inverted = DetectWorking(small, invert: true, out double scoreI);
            var best = scoreI > scoreN ? inverted : normal;

            double inv = 1.0 / scale; // back to full-resolution coordinates
            Point2d Up(Point2d p) => new(p.X * inv, p.Y * inv);
            foreach (var t in best)
            {
                t.Crown = Up(t.Crown); t.Apex = Up(t.Apex);
                t.WidthA = Up(t.WidthA); t.WidthB = Up(t.WidthB);
            }

            // Refine + validate at full resolution: precise crown breadth (the click-mode
            // finder), then keep only lines that really run INSIDE one tooth, then merge
            // duplicates. Without this the detector also drew lines across roots and bone.
            bool Inside(Point2d p) => p.X >= 0 && p.Y >= 0 && p.X < gray8.Width && p.Y < gray8.Height;
            bool Refine(DetectedTooth t)
            {
                if (!Inside(t.Crown) || !Inside(t.Apex)) return false;
                // no clear tooth edges across the line → it isn't lying along a tooth
                // (measured: every wrong line failed this, every correct one passed)
                var b = ToothMeasure.FindCrownBreadth(gray8, t.Crown, t.Apex);
                if (b == null) return false;
                t.WidthA = b.Value.A; t.WidthB = b.Value.B;
                double ratio = t.LengthPx / Math.Max(t.WidthPx, 1);
                return ratio >= 1.6 && ratio <= 6                                        // tooth-shaped
                    && t.LengthPx <= Math.Max(gray8.Width, gray8.Height) * 0.9            // not across the film
                    && RunsInsideTooth(gray8, t);
            }
            var kept = best.Where(Refine).ToList();

            // two half-tooth lines (one crown split by the dark root canal) → one centred line
            best = new List<DetectedTooth>();
            foreach (var group in GroupDuplicates(kept))
            {
                if (group.Count == 1) { best.Add(group[0]); continue; }
                var m = new DetectedTooth
                {
                    Crown = new Point2d(group.Average(g => g.Crown.X), group.Average(g => g.Crown.Y)),
                    Apex = new Point2d(group.Average(g => g.Apex.X), group.Average(g => g.Apex.Y)),
                    WidthA = group[0].WidthA, WidthB = group[0].WidthB,
                };
                best.Add(Refine(m) ? m : group.OrderByDescending(g => g.LengthPx).First());
            }

            return best.OrderBy(t => (t.Crown.X + t.Apex.X) / 2).ToList();
        }

        private static Mat ToGray8(Mat m)
        {
            var g = new Mat();
            if (m.Channels() == 3) Cv2.CvtColor(m, g, ColorConversionCodes.BGR2GRAY);
            else if (m.Channels() == 4) Cv2.CvtColor(m, g, ColorConversionCodes.BGRA2GRAY);
            else m.CopyTo(g);
            if (g.Type() == MatType.CV_16UC1)
            {
                var g8 = new Mat();
                g.ConvertTo(g8, MatType.CV_8U, 1.0 / 257);
                g.Dispose();
                return g8;
            }
            return g;
        }

        private static List<DetectedTooth> DetectWorking(Mat small, bool invert, out double score)
        {
            score = 0;
            var result = new List<DetectedTooth>();

            using var img0 = new Mat();
            if (invert) Cv2.BitwiseNot(small, img0); else small.CopyTo(img0);

            // Clipped white (unexposed cone-cut / corners, clamped by the display conversion):
            // never a tooth. Treated as "no data" from here on.
            using var clipped = new Mat();
            Cv2.Threshold(img0, clipped, 250, 255, ThresholdTypes.Binary);
            using (var big = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15)))
            {
                Cv2.MorphologyEx(clipped, clipped, MorphTypes.Open, big);
                Cv2.Dilate(clipped, clipped, big);
            }
            using var valid0 = new Mat();
            Cv2.BitwiseNot(clipped, valid0);

            Cv2.GaussianBlur(img0, img0, new Size(0, 0), 2.0);
            double airT = OtsuOnMask(img0, valid0) * 0.8;
            using var air0 = new Mat();
            Cv2.Threshold(img0, air0, airT, 255, ThresholdTypes.BinaryInv);
            Cv2.BitwiseAnd(air0, valid0, air0);
            using (var k5 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7)))
                Cv2.MorphologyEx(air0, air0, MorphTypes.Open, k5); // ignore dark specks inside tissue
            if (Cv2.CountNonZero(air0) < img0.Total() * 0.02) return result; // no air side → can't orient

            // ── 1. ORIENT ──
            double axisDeg = TeethAxisDegrees(img0, air0, valid0);
            var center = new Point2f(img0.Width / 2f, img0.Height / 2f);
            // rotate so the teeth axis becomes vertical
            using var rot = Cv2.GetRotationMatrix2D(center, axisDeg - 90, 1.0);
            int side = (int)Math.Ceiling(Math.Sqrt(img0.Width * img0.Width + img0.Height * img0.Height));
            rot.Set(0, 2, rot.Get<double>(0, 2) + (side - img0.Width) / 2.0);
            rot.Set(1, 2, rot.Get<double>(1, 2) + (side - img0.Height) / 2.0);
            var sz = new Size(side, side);
            using var img = new Mat(); Cv2.WarpAffine(img0, img, rot, sz, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
            using var air = new Mat(); Cv2.WarpAffine(air0, air, rot, sz, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
            using var valid = new Mat(); Cv2.WarpAffine(valid0, valid, rot, sz, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));

            // flip so the air (occlusal) side is at the top
            int third = side / 3;
            int airTop = Cv2.CountNonZero(new Mat(air, new Rect(0, 0, side, third)));
            int airBot = Cv2.CountNonZero(new Mat(air, new Rect(0, side - third, side, third)));
            bool flipped = airBot > airTop;
            if (flipped) { Cv2.Flip(img, img, FlipMode.X); Cv2.Flip(air, air, FlipMode.X); Cv2.Flip(valid, valid, FlipMode.X); }

            // tissue = valid, not air
            using var tissue = new Mat();
            Cv2.Subtract(valid, air, tissue);

            var gi = img.GetGenericIndexer<byte>();
            var ti = tissue.GetGenericIndexer<byte>();

            // ── 2. TEETH ACROSS ──
            // per column: the air→tissue boundary (first tissue row from the top) and the mean
            // brightness of the crown band just below it
            int band = (int)(WorkHeight * 0.10);
            var boundary = new int[side];
            var crownProfile = new double[side];
            for (int x = 0; x < side; x++)
            {
                boundary[x] = -1;
                for (int y = 0; y < side; y++) if (ti[y, x] != 0) { boundary[x] = y; break; }
                if (boundary[x] < 0) continue;
                double s = 0; int n = 0;
                for (int y = boundary[x] + 3; y < Math.Min(side, boundary[x] + band); y++)
                    if (ti[y, x] != 0) { s += gi[y, x]; n++; }
                crownProfile[x] = n > band / 3 ? s / n : 0;
            }
            var smoothProfile = Smooth(crownProfile, 4);
            var nonZero = smoothProfile.Where(v => v > 0).OrderBy(v => v).ToArray();
            if (nonZero.Length < side / 10) return result;
            double toothT = OtsuOn(nonZero);

            int minWidth = (int)(WorkHeight * 0.06), maxWidth = (int)(WorkHeight * 0.45);
            var runs = new List<(int x0, int x1)>();
            for (int x = 0; x < side;)
            {
                if (smoothProfile[x] <= toothT) { x++; continue; }
                int x0 = x; while (x < side && smoothProfile[x] > toothT) x++;
                if (x - x0 >= minWidth && x - x0 <= maxWidth) runs.Add((x0, x - 1));
            }

            // map a point in the working (rotated/flipped) frame back to the small image
            using var invRot = new Mat(); Cv2.InvertAffineTransform(rot, invRot);
            double r00 = invRot.Get<double>(0, 0), r01 = invRot.Get<double>(0, 1), r02 = invRot.Get<double>(0, 2);
            double r10 = invRot.Get<double>(1, 0), r11 = invRot.Get<double>(1, 1), r12 = invRot.Get<double>(1, 2);
            Point2d Back(double x, double y)
            {
                if (flipped) y = side - 1 - y;
                return new Point2d(r00 * x + r01 * y + r02, r10 * x + r11 * y + r12);
            }

            foreach (var (x0, x1) in runs)
            {
                int w = x1 - x0 + 1;
                int xc = (x0 + x1) / 2;

                // ── 3. CROWN TIP: highest tissue row near the run's centre ──
                int tipY = int.MaxValue;
                for (int x = xc - w / 6; x <= xc + w / 6; x++) if (boundary[x] >= 0) tipY = Math.Min(tipY, boundary[x]);
                if (tipY == int.MaxValue) continue;

                // breadth: widest bright run around the centre within the crown band
                double rowT = toothT;
                int bestW = 0, bestY = tipY, bestL = x0, bestR = x1;
                for (int y = tipY + 2; y < Math.Min(side, tipY + band * 2); y++)
                {
                    if (gi[y, xc] <= rowT) continue;
                    int l = xc, r = xc;
                    while (l > 0 && gi[y, l - 1] > rowT && ti[y, l - 1] != 0) l--;
                    while (r < side - 1 && gi[y, r + 1] > rowT && ti[y, r + 1] != 0) r++;
                    if (r - l > bestW && r - l <= maxWidth) { bestW = r - l; bestY = y; bestL = l; bestR = r; }
                }
                if (bestW < minWidth) continue;

                // ── 4. APEX: follow the root's centre while it's brighter than its flanks ──
                double cx = (bestL + bestR) / 2.0;
                double crownW = bestR - bestL;
                double refContrast = 0;
                int apexY = bestY, weak = 0;
                int maxLen = (int)(crownW * 4.5);
                for (int y = bestY; y < Math.Min(side - 1, tipY + maxLen); y++)
                {
                    double depth = (y - bestY) / Math.Max(crownW * 3.0, 1);
                    double half = crownW / 2 * Math.Max(0.25, 1.0 - 0.7 * depth); // roots taper
                    double coreHalf = half * 0.5;

                    // re-centre on the brightest window (roots curve slightly)
                    double bestS = double.MinValue; double bestC = cx;
                    for (double c = cx - half * 0.3; c <= cx + half * 0.3; c += 1)
                    {
                        double s = MeanRow(gi, ti, y, c - coreHalf, c + coreHalf, side);
                        if (s > bestS) { bestS = s; bestC = c; }
                    }
                    cx = cx * 0.7 + bestC * 0.3;

                    double core = MeanRow(gi, ti, y, cx - coreHalf, cx + coreHalf, side);
                    double flank = (MeanRow(gi, ti, y, cx - half * 1.8, cx - half * 1.1, side) +
                                    MeanRow(gi, ti, y, cx + half * 1.1, cx + half * 1.8, side)) / 2;
                    double contrast = core - flank;
                    if (y < bestY + crownW * 0.5) { refContrast = Math.Max(refContrast, contrast); continue; }
                    if (refContrast <= 2) break;

                    if (contrast < refContrast * 0.2) { if (++weak > crownW * 0.15) break; }
                    else { weak = 0; apexY = y; }
                }
                if (apexY - tipY < crownW * 1.2) continue; // no root found → not a usable tooth

                result.Add(new DetectedTooth
                {
                    Crown = Back(xc, tipY),
                    Apex = Back(cx, apexY),
                    WidthA = Back(bestL, bestY),
                    WidthB = Back(bestR, bestY),
                });
                score += refContrast * (apexY - tipY) / WorkHeight;
            }
            return result;
        }

        // Dominant axis of teeth (degrees, image coords) from the structure tensor of edges
        // INSIDE tissue — root sides and PDL lines run along the teeth, so their gradients
        // point across them. The crown/air boundary (which runs across the teeth) is excluded.
        private static double TeethAxisDegrees(Mat img, Mat air, Mat valid)
        {
            using var gx = new Mat(); using var gy = new Mat();
            Cv2.Sobel(img, gx, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(img, gy, MatType.CV_32F, 0, 1, 3);
            using var mask = new Mat();
            using (var k = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(21, 21)))
                Cv2.Dilate(air, mask, k);
            Cv2.BitwiseNot(mask, mask);
            Cv2.BitwiseAnd(mask, valid, mask);

            var xi = gx.GetGenericIndexer<float>(); var yi = gy.GetGenericIndexer<float>(); var mi = mask.GetGenericIndexer<byte>();
            double jxx = 0, jyy = 0, jxy = 0;
            for (int y = 0; y < img.Rows; y += 2)
                for (int x = 0; x < img.Cols; x += 2)
                {
                    if (mi[y, x] == 0) continue;
                    double a = xi[y, x], b = yi[y, x];
                    jxx += a * a; jyy += b * b; jxy += a * b;
                }
            double gradAngle = 0.5 * Math.Atan2(2 * jxy, jxx - jyy); // dominant gradient direction
            return gradAngle * 180 / Math.PI + 90;                    // teeth run perpendicular to it
        }

        // Detections of the same tooth: roughly parallel lines whose crown tips are closer
        // (across the axis) than their crown breadths combined — e.g. a crown split in two by
        // the dark pulp chamber gives two half-tooth lines side by side.
        private static List<List<DetectedTooth>> GroupDuplicates(List<DetectedTooth> teeth)
        {
            var groups = new List<List<DetectedTooth>>();
            foreach (var t in teeth.OrderByDescending(t => t.LengthPx))
            {
                var g = groups.FirstOrDefault(gr => gr.Any(k =>
                {
                    double L = Math.Max(k.LengthPx, 1);
                    double ux = (k.Apex.X - k.Crown.X) / L, uy = (k.Apex.Y - k.Crown.Y) / L;
                    double dx = t.Crown.X - k.Crown.X, dy = t.Crown.Y - k.Crown.Y;
                    double along = dx * ux + dy * uy, across = Math.Abs(-dx * uy + dy * ux);
                    double tx = (t.Apex.X - t.Crown.X) / Math.Max(t.LengthPx, 1), ty = (t.Apex.Y - t.Crown.Y) / Math.Max(t.LengthPx, 1);
                    bool parallel = Math.Abs(ux * tx + uy * ty) > 0.94;   // within ~20°
                    return parallel && across < (k.WidthPx + t.WidthPx) * 0.6 && Math.Abs(along) < L * 0.4;
                }));
                if (g != null) g.Add(t); else groups.Add(new List<DetectedTooth> { t });
            }
            return groups;
        }

        // A real tooth line stays inside one tooth: at most points along it, the strip around
        // the line is brighter than the strips just outside the crown breadth on both sides.
        // Lines that cross several roots (alternating with dark PDL gaps) or run through bone fail.
        private static bool RunsInsideTooth(Mat g, DetectedTooth t)
        {
            double L = t.LengthPx, half = t.WidthPx / 2;
            if (L < 10 || half < 3) return false;
            var u = new Point2d((t.Apex.X - t.Crown.X) / L, (t.Apex.Y - t.Crown.Y) / L);
            var v = new Point2d(-u.Y, u.X);
            var gi = g.GetGenericIndexer<byte>();
            double S(double x, double y)
            {
                int xi = (int)x, yi = (int)y;
                return xi < 0 || yi < 0 || xi >= g.Width || yi >= g.Height ? -1 : gi[yi, xi];
            }
            double Mean(Point2d c, double from, double to)
            {
                double s = 0; int n = 0;
                for (double k = from; k <= to; k += Math.Max(1, (to - from) / 6))
                {
                    double val = S(c.X + v.X * k, c.Y + v.Y * k);
                    if (val >= 0) { s += val; n++; }
                }
                return n == 0 ? -1 : s / n;
            }

            int inside = 0, total = 0;
            for (double f = 0.1; f <= 0.85; f += 0.05)
            {
                double taper = 1.0 - 0.5 * f;                 // roots narrow toward the apex
                var c = new Point2d(t.Crown.X + u.X * L * f, t.Crown.Y + u.Y * L * f);
                double core = Mean(c, -half * 0.3 * taper, half * 0.3 * taper);
                double left = Mean(c, -half * 1.6 * taper, -half * 1.15 * taper);
                double right = Mean(c, half * 1.15 * taper, half * 1.6 * taper);
                if (core < 0 || (left < 0 && right < 0)) continue;
                total++;
                double flank = left < 0 ? right : right < 0 ? left : Math.Min(left, right);
                if (core > flank + 2) inside++;
            }
            return total >= 6 && inside >= total * 0.7;
        }

        private static double MeanRow(Mat.Indexer<byte> gi, Mat.Indexer<byte> ti, int y, double xa, double xb, int side)
        {
            int a = Math.Max(0, (int)Math.Round(xa)), b = Math.Min(side - 1, (int)Math.Round(xb));
            double s = 0; int n = 0;
            for (int x = a; x <= b; x++) if (ti[y, x] != 0) { s += gi[y, x]; n++; }
            return n == 0 ? 0 : s / n;
        }

        private static double[] Smooth(double[] v, int r)
        {
            var o = new double[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                double s = 0; int n = 0;
                for (int j = Math.Max(0, i - r); j <= Math.Min(v.Length - 1, i + r); j++) if (v[j] > 0) { s += v[j]; n++; }
                o[i] = v[i] > 0 && n > 0 ? s / n : 0;
            }
            return o;
        }

        private static double OtsuOn(double[] sortedValues)
        {
            var hist = new double[256];
            foreach (var v in sortedValues) hist[Math.Clamp((int)v, 0, 255)]++;
            return OtsuHist(hist);
        }

        // Otsu threshold computed only over the pixels where 'mask' is set
        private static double OtsuOnMask(Mat img, Mat mask)
        {
            var hist = new double[256];
            var idx = img.GetGenericIndexer<byte>();
            var midx = mask.GetGenericIndexer<byte>();
            for (int y = 0; y < img.Rows; y++)
                for (int x = 0; x < img.Cols; x++)
                    if (midx[y, x] != 0) hist[idx[y, x]]++;
            return OtsuHist(hist);
        }

        private static double OtsuHist(double[] hist)
        {
            double total = hist.Sum(); if (total == 0) return 128;
            double sumAll = 0; for (int i = 0; i < 256; i++) sumAll += i * hist[i];
            double wB = 0, sumB = 0, best = 0; int bestT = 128;
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t]; if (wB == 0) continue;
                double wF = total - wB; if (wF == 0) break;
                sumB += t * hist[t];
                double mB = sumB / wB, mF = (sumAll - sumB) / wF;
                double between = wB * wF * (mB - mF) * (mB - mF);
                if (between > best) { best = between; bestT = t; }
            }
            return bestT;
        }
    }
}
