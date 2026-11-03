using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// The owner's character pictures (2026-10-08: char01-04, for the character choice) cut out of their backgrounds into
    /// Resources/UI/Portraits. The delivered files are kept as they are (Art/UI/Portraits); two of them came inside a black
    /// frame, which is an asset-side issue reported to the owner and removed here at import. First the black that touches
    /// the picture's edge goes (the frame), then the white round the character (from the edge and the frame's hole), which
    /// stops at the character's dark outline, so black boots and helmets stay. The white fringe left on the outline is faded
    /// out, and the result is cropped to the character with a small margin.
    /// </summary>
    public static class PortraitImport
    {
        public const string Source = "Assets/Zombie/Art/UI/Portraits";
        public const string Out = "Assets/Zombie/Resources/UI/Portraits";

        [MenuItem("Zombie/Developer (Claude)/Import Character Portraits")]
        public static void Run()
        {
            Directory.CreateDirectory(Out);
            // The four characters (char01-04) and the team picture for the main menu (team).
            foreach (var path in Directory.GetFiles(Source, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                var (cut, frame, white) = Cut(tex);
                var outPath = $"{Out}/{name}.png";
                File.WriteAllBytes(outPath, cut.EncodeToPNG());
                AssetDatabase.ImportAsset(outPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                Debug.Log($"[ZOMBIE-PORTRAIT] {name}: {tex.width}x{tex.height} -> {cut.width}x{cut.height}, frame {frame} px, background {white} px");
            }
        }

        // A picture may come with its own transparency: only solid dark pixels are frame, and see-through ones are background.
        private static bool Dark(Color32 c) => c.a > 200 && c.r < 40 && c.g < 40 && c.b < 40;

        private static bool Light(Color32 c)
        {
            if (c.a < 30) return true;
            var min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return min > 218 && max - min < 24;
        }

        public static (Texture2D cut, int frame, int white) Cut(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            var gone = new bool[w * h];
            var frame = Flood(px, gone, w, h, Edge(w, h), Dark);
            // The white: from the edge, and from every pixel next to the frame just removed.
            var seeds = Edge(w, h);
            for (var i = 0; i < gone.Length; i++)
                if (gone[i]) foreach (var n in Neighbours(i, w, h)) if (!gone[n]) seeds.Add(n);
            var white = Flood(px, gone, w, h, seeds, Light);
            // Fade the light fringe on the outline (the anti-aliasing between the white and the line).
            var alpha = new byte[w * h];
            for (var i = 0; i < px.Length; i++) alpha[i] = gone[i] ? (byte)0 : (byte)255;
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < px.Length; i++)
                {
                    if (alpha[i] == 0) continue;
                    var touches = false;
                    foreach (var n in Neighbours(i, w, h)) if (alpha[n] == 0) touches = true;
                    if (!touches) continue;
                    var c = px[i];
                    var light = Mathf.Min(c.r, Mathf.Min(c.g, c.b)) / 255f;
                    if (light > 0.55f) alpha[i] = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01((0.95f - light) / 0.4f));
                }
            // Crop to the character, with a margin.
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (alpha[y * w + x] < 16 || px[y * w + x].a < 16) continue;
                x0 = Mathf.Min(x0, x);
                y0 = Mathf.Min(y0, y);
                x1 = Mathf.Max(x1, x);
                y1 = Mathf.Max(y1, y);
            }
            const int margin = 12;
            x0 = Mathf.Max(0, x0 - margin);
            y0 = Mathf.Max(0, y0 - margin);
            x1 = Mathf.Min(w - 1, x1 + margin);
            y1 = Mathf.Min(h - 1, y1 + margin);
            var cw = x1 - x0 + 1;
            var ch = y1 - y0 + 1;
            var outPx = new Color32[cw * ch];
            for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
            {
                var i = (y + y0) * w + x + x0;
                var c = px[i];
                c.a = (byte)(alpha[i] * c.a / 255);
                outPx[y * cw + x] = c;
            }
            var cut = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            cut.SetPixels32(outPx);
            cut.Apply();
            return (cut, frame, white);
        }

        private static List<int> Edge(int w, int h)
        {
            var list = new List<int>();
            for (var x = 0; x < w; x++)
            {
                list.Add(x);
                list.Add((h - 1) * w + x);
            }
            for (var y = 0; y < h; y++)
            {
                list.Add(y * w);
                list.Add(y * w + w - 1);
            }
            return list;
        }

        private static IEnumerable<int> Neighbours(int i, int w, int h)
        {
            var x = i % w;
            var y = i / w;
            if (x > 0) yield return i - 1;
            if (x < w - 1) yield return i + 1;
            if (y > 0) yield return i - w;
            if (y < h - 1) yield return i + w;
        }

        /// <summary>Marks gone every pixel passing <paramref name="pass"/> reachable from the seeds; returns how many.</summary>
        private static int Flood(Color32[] px, bool[] gone, int w, int h, List<int> seeds, System.Func<Color32, bool> pass)
        {
            var stack = new Stack<int>();
            foreach (var s in seeds) if (!gone[s] && pass(px[s])) stack.Push(s);
            var n = 0;
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (gone[i]) continue;
                gone[i] = true;
                n++;
                foreach (var m in Neighbours(i, w, h)) if (!gone[m] && pass(px[m])) stack.Push(m);
            }
            return n;
        }
    }
}
