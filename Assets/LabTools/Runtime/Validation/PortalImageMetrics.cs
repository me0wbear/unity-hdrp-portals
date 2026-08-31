using System;
using UnityEngine;

namespace Portals.Lab.Validation
{
    [Serializable]
    public sealed class PortalImageDifference
    {
        public double redMae;
        public double greenMae;
        public double blueMae;
        public int maxChannelDifference;
        public int pixelCount;

        public bool IsValid(int expectedPixels) => pixelCount == expectedPixels && pixelCount > 0
            && ValidChannel(redMae) && ValidChannel(greenMae) && ValidChannel(blueMae)
            && maxChannelDifference >= 0 && maxChannelDifference <= 255;

        private static bool ValidChannel(double value) => PortalCheckPolicy.Finite(value) && value >= 0 && value <= 255;
    }

    public static class PortalImageMetrics
    {
        // RGB сравнивается в единицах 8-битного capture, без утверждений о HDR radiance.
        public static PortalImageDifference Compare(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
                throw new ArgumentException("RGB images must have equal, nonempty pixel arrays.");
            long r = 0, g = 0, blue = 0;
            int maximum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int dr = Math.Abs(a[i].r - b[i].r), dg = Math.Abs(a[i].g - b[i].g), db = Math.Abs(a[i].b - b[i].b);
                r += dr; g += dg; blue += db;
                maximum = Math.Max(maximum, Math.Max(dr, Math.Max(dg, db)));
            }
            return new PortalImageDifference { redMae = (double)r / a.Length, greenMae = (double)g / a.Length,
                blueMae = (double)blue / a.Length, maxChannelDifference = maximum, pixelCount = a.Length };
        }

        public static RectInt FromTopLeft(int imageWidth, int imageHeight, int x, int y, int width, int height)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || x < 0 || y < 0 || width <= 0 || height <= 0
                || (long)x + width > imageWidth || (long)y + height > imageHeight)
                throw new ArgumentException("ROI must fit inside the image without clamping.");
            return new RectInt(x, imageHeight - y - height, width, height);
        }

        public static int CountNewMagenta(Color32[] image, Color32[] background)
        {
            if (image == null || background == null || image.Length == 0 || image.Length != background.Length)
                throw new ArgumentException("Marker control images must have equal, nonempty pixel arrays.");
            int count = 0;
            for (int i = 0; i < image.Length; i++) if (Magenta(image[i]) && !Magenta(background[i])) count++;
            return count;
        }

        private static bool Magenta(Color32 pixel) => pixel.r >= 128 && pixel.b >= 128 && pixel.g <= 96
            && pixel.r - pixel.g >= 64 && pixel.b - pixel.g >= 64;

        public static Vector3 LeakageMarkerPosition(Vector3 eye, Vector3 forward, Vector3 planePoint, Vector3 normal)
        {
            float denominator = Vector3.Dot(forward.normalized, normal.normalized);
            float distance = Vector3.Dot(planePoint - eye, normal.normalized) / denominator;
            if (!PortalCheckPolicy.Finite(distance) || Mathf.Abs(denominator) < 0.0001f || distance <= 0.1f)
                throw new ArgumentException("Exit plane must lie in front of the mapped eye beyond the near plane.");
            return eye + forward.normalized * (distance * 0.5f);
        }
    }
}
