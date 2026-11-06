using OpenCvSharp;
using System;
using System.Linq;

namespace DentalX
{
    public static class ToothMeasure
    {
        // ═══════════════════════════════════════════════════
        //  CROWN BREADTH for a tooth whose crown tip and apex the dentist clicked.
        //
        //  With the tooth's axis known, finding its width is a 1-D problem: sample brightness
        //  along lines at right angles to the axis through the crown (5–40% of the length down
        //  from the tip), find the tooth's two side edges on each line — the strongest
        //  bright→dark step going outward, searched from a little way out from the centre so
        //  the dark pulp chamber can't be taken for an edge — and keep the widest line.
        //
        //  'image' is the image being viewed (8- or 16-bit grey); points are image pixels.
        //  Returns null when no clear edges are found (e.g. the clicks aren't on a tooth).
        // ═══════════════════════════════════════════════════
        public static (Point2d A, Point2d B)? FindCrownBreadth(Mat image, Point2d crown, Point2d apex)
        {
            using var g = ToGray8(image);
            Cv2.GaussianBlur(g, g, new Size(0, 0), Math.Max(1.0, g.Height / 400.0));
            var gi = g.GetGenericIndexer<byte>();

            double L = Math.Sqrt((apex.X - crown.X) * (apex.X - crown.X) + (apex.Y - crown.Y) * (apex.Y - crown.Y));
            if (L < 10) return null;
            var u = new Point2d((apex.X - crown.X) / L, (apex.Y - crown.Y) / L); // along the tooth
            var v = new Point2d(-u.Y, u.X);                                       // across it

            double Sample(double x, double y)
            {
                int xi = (int)Math.Round(x), yi = (int)Math.Round(y);
                if (xi < 0 || yi < 0 || xi >= g.Width || yi >= g.Height) return -1;
                return gi[yi, xi];
            }

            // a crown is roughly 25–60% as wide as the tooth is long (incisor ≈ 0.35, molar ≈ 0.5)
            int maxHalf = (int)Math.Ceiling(L * 0.40), minHalf = (int)(L * 0.06);
            var lines = new System.Collections.Generic.List<(double w, Point2d a, Point2d b)>();

            // from just below the biting edge (which runs along air, not across the tooth)
            for (double t = 0.10; t <= 0.40; t += 0.025)
            {
                var c = new Point2d(crown.X + u.X * L * t, crown.Y + u.Y * L * t);

                // brightness profile across the tooth, averaged over 3 parallel lines
                var prof = new double[2 * maxHalf + 1];
                for (int s = -maxHalf; s <= maxHalf; s++)
                {
                    double sum = 0; int n = 0;
                    for (int o = -1; o <= 1; o++)
                    {
                        double val = Sample(c.X + v.X * s + u.X * o * 2, c.Y + v.Y * s + u.Y * o * 2);
                        if (val >= 0) { sum += val; n++; }
                    }
                    prof[s + maxHalf] = n > 0 ? sum / n : -1;
                }

                // The tooth's OWN side edge is the first strong bright→dark step going outward;
                // taking the strongest step overall could jump the gap to the neighbour's far edge.
                int Edge(int sign)
                {
                    var steps = new double[maxHalf];
                    double maxStep = 0;
                    for (int s = minHalf; s < maxHalf - 2; s++)
                    {
                        double inner = prof[maxHalf + sign * s], outer = prof[maxHalf + sign * (s + 2)];
                        if (inner < 0 || outer < 0) break;
                        steps[s] = inner - outer;
                        maxStep = Math.Max(maxStep, steps[s]);
                    }
                    if (maxStep < 4) return 0;                // 4 grey levels: a real edge, not noise
                    for (int s = minHalf; s < maxHalf - 2; s++)
                    {
                        if (steps[s] < maxStep * 0.5) continue;
                        while (s + 1 < maxHalf - 2 && steps[s + 1] > steps[s]) s++; // peak of this edge
                        return s + 1;                          // middle of the 2-px step
                    }
                    return 0;
                }

                int left = Edge(-1), right = Edge(+1);
                if (left == 0 || right == 0) continue;
                lines.Add((left + right,
                           new Point2d(c.X - v.X * left, c.Y - v.Y * left),
                           new Point2d(c.X + v.X * right, c.Y + v.Y * right)));
            }
            if (lines.Count == 0) return null;

            // widest crown line, ignoring outliers (an edge missed on one line shows up as a
            // width well above the others)
            var widths = lines.Select(l => l.w).OrderBy(w => w).ToList();
            double median = widths[widths.Count / 2];
            var best = lines.Where(l => l.w <= median * 1.25).OrderByDescending(l => l.w).First();
            return (best.a, best.b);
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
    }
}
