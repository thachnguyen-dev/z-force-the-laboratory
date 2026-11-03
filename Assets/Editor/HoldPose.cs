using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZombieGame.Art;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Weapon grips for the owner's rigged characters (2026-10-06; editor only for now, not connected to the game).
    /// Poses a character's arms, hands and fingers so it really holds the weapon, after real-world technique:
    /// - Rifle, shouldered: the stock in the right shoulder pocket; the right hand high on the pistol grip, middle,
    ///   ring and little fingers wrapped round it, the thumb round the other side, the index finger on the trigger;
    ///   the left (support) hand under the handguard, palm up, fingers wrapping up its far side, thumb along the near side.
    /// - Pistol, isosceles: both arms out; the right hand high on the grip, fingers wrapped, index on the trigger; the
    ///   support hand wrapping over the right hand's fingers, both thumbs forward along the frame.
    /// Works on any rest orientation of the owner's bones: everything is done in world space from the T-pose.
    /// </summary>
    public static partial class HoldPose
    {
        private static string OutDir
        {
            get
            {
                var d = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        // ------------------------------------------------------------------ the character, coloured (editor preview)

        /// <summary>
        /// Region colours (sRGB) for character 02, Marine Corps (owner, 2026-10-06: coyote gloves and boots rejected):
        /// ranger-green gloves and black boots, which sit with the green digital camouflage. Every material not listed
        /// here is camouflage (owner rule: whatever isn't assigned elsewhere is cloth).
        /// </summary>
        public static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            ["gloves"] = new Color(0.22f, 0.24f, 0.17f),
            ["boots"] = new Color(0.1f, 0.095f, 0.085f),
            ["hair"] = new Color(0.4f, 0.26f, 0.15f),
            ["mask"] = new Color(0.14f, 0.15f, 0.13f),
            ["glass"] = new Color(0.16f, 0.2f, 0.22f),
            ["skin"] = new Color(0.85f, 0.66f, 0.52f),
            ["helmet"] = new Color(0.33f, 0.35f, 0.24f),
            ["gear"] = new Color(0.35f, 0.33f, 0.22f),
            ["metal"] = new Color(0.4f, 0.42f, 0.42f),
            // Character 04's swords on the back (scabbards and hilts): dark gunmetal.
            ["sword"] = new Color(0.17f, 0.17f, 0.19f),
        };

        private const float PatternScale = 4f;

        public static string Region(Material m)
        {
            var name = m != null ? m.name.Replace(" (Instance)", "").ToLowerInvariant() : "cloth";
            // Blender's duplicate suffixes (boots.001, mask.002) name the same region.
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\.\d+$", "");
            // The same region under another spelling (zombie_2's 'pant' is zombie_1's 'pants').
            return name == "pant" ? "pants" : name;
        }

        /// <summary>
        /// The character instance's mesh, coloured: region colours as vertex colours, the camouflage on the rest
        /// (pattern coordinates fixed to the body). No eyes (owner, 2026-10-06: removed). Returns a report line.
        /// </summary>
        /// <summary>
        /// What an unmarked part (a material region with no colour of its own) gets, by character group (owner,
        /// 2026-10-06): the main characters their branch camouflage (null here), the scientist black, zombies green,
        /// fast zombies red. Marked regions take their colour from <see cref="Colours"/> (chosen from references).
        /// </summary>
/// <summary>
        /// Group colours for the secondary characters (owner, 2026-10-06: colours from real-world references), used
        /// before <see cref="Colours"/>. Scientist: white lab coat, blue nitrile lab gloves, vials of green liquid.
        /// Zombies (fast ones too): desaturated sickly-green skin, muted pinkish-purple shirt, deep purple-blue trousers,
        /// pale yellow teeth and eyes. `cloth` is the unmarked region (see <see cref="UnmarkedColour"/>).
        /// </summary>
        private static readonly Dictionary<string, Color> ZombieColours = new Dictionary<string, Color>
        {
            ["skin"] = new Color(0.55f, 0.66f, 0.45f),
            ["shirt"] = new Color(0.55f, 0.45f, 0.55f),
            ["pants"] = new Color(0.25f, 0.25f, 0.42f),
            ["teeth"] = new Color(0.86f, 0.82f, 0.55f),
            ["eye"] = new Color(0.9f, 0.88f, 0.62f),
            // zombie_2 (2026-10-07): worn, dirty dark-brown leather boots; dull, dark matted hair.
            ["boots"] = new Color(0.22f, 0.18f, 0.14f),
            ["hair"] = new Color(0.17f, 0.14f, 0.12f),
        };

        /// <summary>The four-legged animals (owner, 2026-10-07: one material region each, coloured by bone and place).</summary>
        public static bool IsAnimal(string model) => model == "zmb_dog" || model == "zmb_rat" || model == "boss_tiger";

        /// <summary>
        /// The animals being zombified, in the game's infection green (owner, 2026-10-07). References: a stray dog's dull grey-brown coat;
        /// a brown rat's grey-brown fur with a pink, scaly tail and pink feet; a tiger's orange coat with black stripes and
        /// a white belly. The zombification: fur rotting off in patches over the zombies' sickly green skin, near infection sites that
        /// glow green, with dark green veins. Returns the colour (sRGB) and how much it glows.
        /// </summary>
        /// <summary>
        /// The animals' separate pieces, by what they are (found 2026-10-07 by rendering each piece; the model has one
        /// material region): the largest piece is the body; on the jaw the teeth (the dog's largest jaw piece is its
        /// tongue); the tiger's pieces on the head are its eyes, its largest other piece the collar, those on the hind legs
        /// the scratches on its back; any other small piece is a shard of exposed bone. Per vertex, "" for the body.
        /// </summary>
        private static string[] AnimalParts(string model, SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            var t = mesh.triangles;
            var w = mesh.boneWeights;
            var parts = new string[mesh.vertexCount];
            for (var i = 0; i < parts.Length; i++) parts[i] = "";
            var pieces = PackParts.Split(mesh).OrderByDescending(p => p.Triangles.Count).ToList();
            var jawBiggest = true;
            var otherBiggest = true;
            for (var k = 1; k < pieces.Count; k++)
            {
                var idx = pieces[k].Triangles.SelectMany(x => new[] { t[x], t[x + 1], t[x + 2] }).Distinct().ToList();
                var bone = idx.GroupBy(i => smr.bones[w[i].boneIndex0].name).OrderByDescending(g => g.Count()).First().Key;
                string what;
                if (bone == "Jaw")
                {
                    what = model == "zmb_dog" && jawBiggest ? "tongue" : "teeth";
                    jawBiggest = false;
                }
                else if (model == "boss_tiger" && bone == "Head") what = "eye";
                else if (model == "boss_tiger" && bone.StartsWith("Hind")) what = "wound";
                else if (model == "boss_tiger" && otherBiggest) { what = "collar"; otherBiggest = false; }
                else what = "bone";
                foreach (var i in idx) parts[i] = what;
            }
            return parts;
        }

        private static (Color colour, float glow) AnimalColour(string model, string bone, Vector3 p, Vector3 n, float height, string part = "")
        {
            // The separate pieces first: teeth, the tongue, eyes, the collar, wounds, bone shards.
            var grain = Noise3(p * 40f);
            switch (part)
            {
                case "teeth":
                    // A rat's incisors are orange-yellow; the others' dirty ivory.
                    return (model == "zmb_rat" ? Color.Lerp(new Color(0.78f, 0.5f, 0.2f), new Color(0.9f, 0.68f, 0.3f), grain)
                        : Color.Lerp(new Color(0.7f, 0.64f, 0.48f), new Color(0.9f, 0.86f, 0.7f), grain), 0f);
                case "tongue":
                    // Dead, bruised and turning blue-grey.
                    return (Color.Lerp(new Color(0.36f, 0.24f, 0.32f), new Color(0.52f, 0.36f, 0.45f), grain), 0f);
                case "eye":
                    return (new Color(0.62f, 1f, 0.35f), 0.6f);
                case "collar":
                    // A worn, cracked dark leather collar (an escaped zoo animal).
                    return (Color.Lerp(new Color(0.14f, 0.1f, 0.07f), new Color(0.3f, 0.21f, 0.14f), grain), 0f);
                case "wound":
                {
                    var wc = Color.Lerp(new Color(0.1f, 0.3f, 0.05f), new Color(0.6f, 1f, 0.3f), Mathf.SmoothStep(0.3f, 0.75f, grain));
                    return (wc, Mathf.Lerp(0.1f, 0.5f, grain));
                }
                case "bone":
                    return (Color.Lerp(new Color(0.5f, 0.45f, 0.34f), new Color(0.76f, 0.7f, 0.55f), grain), 0f);
            }
            var clump = Noise3(new Vector3(p.x * 30f, p.y * 9f, p.z * 30f)) * 0.65f + Noise3(p * 6f) * 0.35f;
            var tail = bone.StartsWith("Tail");
            var foot = bone.EndsWith("Foot.L") || bone.EndsWith("Foot.R") || bone.StartsWith("HindAnkle");
            var belly = n.y < -0.35f && p.y < height * 0.6f;
            Color fur;
            switch (model)
            {
                case "zmb_rat":
                    fur = tail || foot ? Color.Lerp(new Color(0.72f, 0.48f, 0.48f), new Color(0.88f, 0.62f, 0.6f), clump)
                        : belly ? Color.Lerp(new Color(0.55f, 0.52f, 0.47f), new Color(0.7f, 0.67f, 0.6f), clump)
                        : Color.Lerp(new Color(0.24f, 0.21f, 0.18f), new Color(0.47f, 0.42f, 0.36f), clump);
                    break;
                case "boss_tiger":
                {
                    // Stripes: thin dark bands across the body and round the legs and tail, wavy.
                    // About a dozen bands along the body, each a little wavy and tapering towards the belly.
                    var wave = (Noise3(p * 2.5f) - 0.5f) * 0.6f;
                    var band = Mathf.Abs(Mathf.Sin((p.z / height * 4.5f + wave) * Mathf.PI));
                    var stripe = band < Mathf.Lerp(0.05f, 0.2f, Mathf.InverseLerp(height * 0.2f, height * 0.8f, p.y)) && !belly;
                    var orange = Color.Lerp(new Color(0.72f, 0.36f, 0.1f), new Color(0.88f, 0.52f, 0.18f), clump);
                    fur = stripe ? new Color(0.08f, 0.07f, 0.06f) : belly ? Color.Lerp(new Color(0.8f, 0.76f, 0.66f), new Color(0.93f, 0.9f, 0.82f), clump) : orange;
                    break;
                }
                default:
                    fur = belly ? Color.Lerp(new Color(0.42f, 0.38f, 0.32f), new Color(0.6f, 0.55f, 0.47f), clump)
                        : Color.Lerp(new Color(0.2f, 0.18f, 0.16f), new Color(0.45f, 0.4f, 0.33f), clump);
                    if (foot) fur *= 0.7f;
                    break;
            }
            fur.a = 1f;
            // Infection sites: glowing green spots (sized to the animal); round them the fur has rotted to the zombies'
            // green skin with dark veins.
            var q = p / height;
            var site = Noise3(q * 6f + new Vector3(2.2f, 0.7f, 5.1f));
            if (site > 0.8f)
            {
                var core = Mathf.SmoothStep(0.8f, 0.9f, site);
                return (Color.Lerp(new Color(0.1f, 0.3f, 0.05f), new Color(0.6f, 1f, 0.3f), core), Mathf.Lerp(0.1f, 0.55f, core));
            }
            var near = Mathf.Clamp01((site - 0.55f) / 0.25f);
            var bare = near * Mathf.SmoothStep(0f, 1f, Noise3(q * 20f));
            var skin = Color.Lerp(new Color(0.4f, 0.52f, 0.3f), new Color(0.58f, 0.7f, 0.44f), Noise3(p * 22f));
            var c = Color.Lerp(fur, skin, bare);
            var vein = (1f - Mathf.Clamp01(Mathf.Abs(Noise3(q * 18f) - 0.5f) / 0.035f)) * near;
            return (Color.Lerp(c, new Color(0.1f, 0.28f, 0.05f), vein * 0.9f), vein * 0.15f);
        }

        /// <summary>
        /// The groups' colours, most specific prefix first; a character takes a region's colour from the first group
        /// that has it. Owner, 2026-10-07: bosses are zombies (boss_1: the zombie palette); the Bear: brown bear fur,
        /// green infected wounds ('hurt'), dark paws ('hand') and light claws ('foot', as a grizzly's), zombie eyes.
        /// </summary>
        public static readonly (string prefix, Dictionary<string, Color> colours)[] GroupColours =
        {
            ("npc_scientist", new Dictionary<string, Color>
            {
                ["shirts"] = new Color(0.9f, 0.91f, 0.9f),
                ["gloves"] = new Color(0.33f, 0.52f, 0.82f),
                ["medicine"] = new Color(0.35f, 0.78f, 0.4f),
            }),
            ("boss_bear", new Dictionary<string, Color>
            {
                ["skin"] = new Color(0.373f, 0.294f, 0.235f),
                ["hurt"] = new Color(0.42f, 0.75f, 0.25f),
                ["hand"] = new Color(0.17f, 0.16f, 0.16f),
                ["foot"] = new Color(0.8f, 0.76f, 0.64f),
            }),
            ("zmb_", ZombieColours),
            ("boss_", ZombieColours),
        };

        /// <summary>
        /// Owner, 2026-10-07: fast zombies use the regular zombie models (zmb_1, zmb_2) with the green skin red.
        /// </summary>
        public static bool FastVariant;
        private static readonly Color FastSkin = new Color(0.7f, 0.18f, 0.16f);

        /// <summary>A region's colour for a character: its group's colour first, then the shared palette.</summary>
        private static bool RegionColour(string model, string region, out Color c)
        {
            if (FastVariant && region == "skin" && model.StartsWith("zmb_"))
            {
                c = FastSkin;
                return true;
            }
            foreach (var (prefix, colours) in GroupColours)
                if (model.StartsWith(prefix) && colours.TryGetValue(region, out c)) return true;
            if (UnmarkedColour(model).HasValue && region == "cloth")
            {
                c = default;
                return false;
            }
            return Colours.TryGetValue(region, out c);
        }

        public static Color? UnmarkedColour(string model)
        {
            if (model.StartsWith("npc_scientist")) return new Color(0.06f, 0.06f, 0.065f);
            if (model.StartsWith("zmb_") && FastVariant) return new Color(0.62f, 0.09f, 0.07f);
            if (model.StartsWith("zmb_") || model.StartsWith("boss_")) return new Color(0.33f, 0.47f, 0.22f);
            return null;
        }

        /// <summary>
        /// The Bear being zombified, not an injured bear (owner, 2026-10-07), in the game's infection green. References
        /// (zombie creature concept art): fur rotting away in patches over the zombies' sickly green skin;
        /// dark veins of the infection spreading through it; the infection sites ('hurt') glowing green, darker at
        /// their rims; the eyes glowing the same green; yellowed, dirty claws; matted dark-brown fur where it is left.
        /// Returns the colour (sRGB) and how much it glows.
        /// </summary>
        private static (Color colour, float glow) BearColour(string region, Vector3 p, float hurtDistance)
        {
            // Clumps: noise stretched down the body, like matted fur hanging in locks.
            var clump = Noise3(new Vector3(p.x * 34f, p.y * 9f, p.z * 34f)) * 0.65f + Noise3(p * 6f) * 0.35f;
            // Near an infection site (within 25 cm) the virus has taken more of the body.
            var near = Mathf.Clamp01(1f - hurtDistance / 0.25f);
            // Veins: thin dark lines where a noise field crosses its middle.
            var vein = 1f - Mathf.Clamp01(Mathf.Abs(Noise3(p * 14f) - 0.5f) / 0.035f);
            switch (region)
            {
                case "hurt":
                {
                    var n = Noise3(p * 40f) * 0.7f + Noise3(p * 12f) * 0.3f;
                    var core = Color.Lerp(new Color(0.1f, 0.3f, 0.05f), new Color(0.6f, 1f, 0.3f), Mathf.SmoothStep(0.3f, 0.75f, n));
                    return (core, Mathf.Lerp(0.1f, 0.55f, Mathf.SmoothStep(0.45f, 0.9f, n)));
                }
                case "eye":
                    return (new Color(0.62f, 1f, 0.35f), 0.6f);
                case "foot":
                    return (Color.Lerp(new Color(0.36f, 0.32f, 0.24f), new Color(0.68f, 0.63f, 0.48f), clump), 0f);
                case "hand":
                    return (Color.Lerp(new Color(0.06f, 0.05f, 0.05f), new Color(0.19f, 0.15f, 0.12f), clump), 0f);
                default:
                {
                    var fur = Color.Lerp(new Color(0.1f, 0.08f, 0.065f), new Color(0.36f, 0.26f, 0.17f), clump);
                    // Dead skin where the fur has rotted off: patches, more of them near the infection.
                    var bare = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Noise3(p * 12f + new Vector3(3.1f, 0f, 1.7f)) - (1.02f - near * 0.6f)) / 0.05f));
                    var skin = Color.Lerp(new Color(0.4f, 0.52f, 0.3f), new Color(0.58f, 0.7f, 0.44f), Noise3(p * 22f));
                    var c = Color.Lerp(fur, skin, bare);
                    // Infection veins through the dead skin and near the sites, faintly glowing.
                    var v = vein * Mathf.Max(bare, near);
                    return (Color.Lerp(c, new Color(0.1f, 0.28f, 0.05f), v * 0.9f), v * 0.15f);
                }
            }
        }

        /// <summary>
        /// The humanoid zombies (and boss_1) decayed rather than flat (owner, 2026-10-07). References (zombie concept
        /// art: decomposing, bruised, discoloured skin; filthy torn clothes): the skin mottled with darker, bruised
        /// patches and dark veins; the clothes stained with dirt and grime, worn lighter where torn. Returns the colour
        /// (sRGB) and the wear (grime, cracks, chips, rust) for the shader.
        /// </summary>
        private static (Color colour, Vector4 wear) ZombieDecay(string region, Color c, Vector3 p)
        {
            switch (region)
            {
                case "skin":
                {
                    var bruise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Noise3(p * 6f + new Vector3(1.3f, 2.1f, 0.4f)) - 0.55f) / 0.15f));
                    var dark = new Color(c.r * 0.55f + 0.08f, c.g * 0.5f + 0.04f, c.b * 0.6f + 0.1f);
                    var col = Color.Lerp(c, dark, bruise * 0.8f);
                    var vein = 1f - Mathf.Clamp01(Mathf.Abs(Noise3(p * 13f) - 0.5f) / 0.03f);
                    return (Color.Lerp(col, col * 0.55f, vein * 0.7f), new Vector4(0.25f, 0f, 0f, 0f));
                }
                case "teeth":
                case "eye":
                    return (c, new Vector4(0.3f, 0f, 0f, 0f));
                default:
                {
                    // Dirt stains, browner and darker.
                    var stain = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Noise3(p * 5f + new Vector3(0.7f, 0f, 2.9f)) - 0.5f) / 0.2f));
                    var col = Color.Lerp(c, new Color(0.28f, 0.22f, 0.15f), stain * 0.55f);
                    return (col, new Vector4(0.75f, 0.15f, 0.35f, 0f));
                }
            }
        }

        /// <summary>
        /// Shading the model's own shape into its colours (owner, 2026-10-07: "do not color it as one uniform block"):
        /// a slow and a fine tonal variation, creases and hollows darker (each vertex against its neighbours), and a soft
        /// height gradient (darker towards the feet). A multiplier per vertex of the delivered mesh, about 0.6 to 1.1.
        /// </summary>
        private static float[] Tone(Mesh src, Vector3[] world, Transform t)
        {
            var n = world.Length;
            var sum = new Vector3[n];
            var cnt = new int[n];
            var edge = 0f;
            var tri = src.triangles;
            for (var k = 0; k < tri.Length; k += 3)
                for (var e = 0; e < 3; e++)
                {
                    int a = tri[k + e], b = tri[k + (e + 1) % 3];
                    sum[a] += world[b];
                    cnt[a]++;
                    sum[b] += world[a];
                    cnt[b]++;
                    edge += (world[a] - world[b]).magnitude;
                }
            edge /= Mathf.Max(1, tri.Length);
            var normals = src.normals;
            var minY = world.Min(p => p.y);
            var maxY = world.Max(p => p.y);
            var tone = new float[n];
            for (var i = 0; i < n; i++)
            {
                // Hollow: the neighbours' middle lies in front of the surface (along its normal).
                var nrm = t.TransformDirection(normals[i]).normalized;
                var hollow = cnt[i] == 0 ? 0f : Vector3.Dot(nrm, sum[i] / cnt[i] - world[i]) / Mathf.Max(0.001f, edge);
                var crease = 1f - 0.5f * Mathf.Clamp01(hollow * 2.5f);
                var variation = 0.8f + 0.3f * Noise3(world[i] * 5f) + 0.14f * (Noise3(world[i] * 23f) - 0.5f);
                var height = Mathf.Lerp(0.72f, 1.08f, Mathf.InverseLerp(minY, maxY, world[i].y));
                tone[i] = crease * variation * height;
            }
            return tone;
        }

        /// <summary>Smooth 3D value noise in 0..1.</summary>
        internal static float Noise3(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float H(int x, int y, int z)
            {
                var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
            float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(H(x0, y0, z0), H(x0 + 1, y0, z0), fx), L(H(x0, y0 + 1, z0), H(x0 + 1, y0 + 1, z0), fx), fy),
                     L(L(H(x0, y0, z0 + 1), H(x0 + 1, y0, z0 + 1), fx), L(H(x0, y0 + 1, z0 + 1), H(x0 + 1, y0 + 1, z0 + 1), fx), fy), fz);
        }

        public static string Colour(SkinnedMeshRenderer smr, Texture2D camo, bool debugGloves = false, Color? unmarked = null, string model = "")
        {
            var src = smr.sharedMesh;
            var rest = new Mesh();
            smr.BakeMesh(rest, true);
            var restV = rest.vertices;
            Object.DestroyImmediate(rest);
            var world = restV.Select(v => smr.transform.TransformPoint(v)).ToArray();
            var srcV = src.vertices;
            var srcN = src.normals;
            var srcW = src.boneWeights;
            var tone = Tone(src, world, smr.transform);
            var worldTop = world.Max(p => p.y);
            var animalParts = IsAnimal(model) ? AnimalParts(model, smr) : null;
            // The Bear: how far each vertex is from the nearest infected wound ('hurt'), for the infection spreading
            // into the fur around it.
            var hurtNear = Enumerable.Repeat(float.MaxValue, world.Length).ToArray();
            if (model.StartsWith("boss_bear"))
            {
                var wounds = new List<Vector3>();
                for (var s = 0; s < src.subMeshCount; s++)
                    if (s < smr.sharedMaterials.Length && Region(smr.sharedMaterials[s]) == "hurt")
                        foreach (var i in src.GetTriangles(s).Distinct()) wounds.Add(world[i]);
                for (var i = 0; i < world.Length; i++)
                    foreach (var w in wounds) hurtNear[i] = Mathf.Min(hurtNear[i], (world[i] - w).sqrMagnitude);
                for (var i = 0; i < world.Length; i++) hurtNear[i] = Mathf.Sqrt(hurtNear[i]);
            }
            // Each region gets its own copy of the vertices it uses: a vertex on the border between two regions is
            // shared in the delivered mesh, and one colour per vertex would let the later region take the border
            // (owner, 2026-10-06: character 03's mask came out smaller than its region).
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var colours = new List<Color>();
            var wear = new List<Vector4>();
            var camoW = new List<Vector2>();
            var pattern = new List<Vector3>();
            var tris = new List<int>();
            var report = new StringBuilder();
            for (var s = 0; s < src.subMeshCount; s++)
            {
                var region = Region(s < smr.sharedMaterials.Length ? smr.sharedMaterials[s] : null);
                var camoRegion = !RegionColour(model, region, out var c);
                // Secondary characters: an unmarked part is a plain colour, not camouflage.
                if (camoRegion && unmarked.HasValue)
                {
                    c = unmarked.Value;
                    camoRegion = false;
                }
                // Checking renders: bright orange gloves, so the hands read against the camouflage.
                if (debugGloves && region == "gloves") c = new Color(1f, 0.45f, 0.05f);
                if (camoRegion) c = new Color(0.4f, 0.42f, 0.3f);
                var lin = c.linear;
                var t = src.GetTriangles(s);
                var copy = new Dictionary<int, int>();
                foreach (var i in t)
                {
                    if (!copy.TryGetValue(i, out var n))
                    {
                        n = verts.Count;
                        copy[i] = n;
                        verts.Add(srcV[i]);
                        normals.Add(srcN[i]);
                        weights.Add(srcW[i]);
                        // Not one flat block (owner, 2026-10-07): a fur blends between its tones by a fine pattern;
                        // every painted region then takes the vertex's tone (variation, creases, height).
                        // Any other region: its colour blends between a cooler shade and a warmer light (painted
                        // stylised models do this rather than one flat fill).
                        var shade = Noise3(world[i] * 9f) * 0.6f + Noise3(world[i] * 31f) * 0.4f;
                        var vc = Color.Lerp(new Color(c.r * 0.72f, c.g * 0.74f, c.b * 0.82f), new Color(Mathf.Min(1f, c.r * 1.12f), Mathf.Min(1f, c.g * 1.1f), c.b * 1.0f), shade);
                        if (debugGloves && region == "gloves") vc = c;
                        var glow = 0f;
                        var vw = camoRegion ? new Vector4(0.2f, 0f, 0.1f, 0f) : new Vector4(0.3f, 0f, 0.2f, 0f);
                        if (IsAnimal(model))
                            (vc, glow) = AnimalColour(model, smr.bones[srcW[i].boneIndex0] != null ? smr.bones[srcW[i].boneIndex0].name : "",
                                world[i], smr.transform.TransformDirection(srcN[i]).normalized, worldTop, animalParts[i]);
                        else if (model.StartsWith("boss_bear")) (vc, glow) = BearColour(region, world[i], hurtNear[i]);
                        else if (model.StartsWith("zmb_") || model.StartsWith("boss_")) (vc, vw) = ZombieDecay(region, vc, world[i]);
                        // Camouflage: the pattern comes from the shader; the vertex colour is its shading (_CamoShade).
                        var col = camoRegion ? new Color(tone[i], tone[i], tone[i]) : (vc * tone[i]).linear;
                        col.a = 1f - glow;
                        colours.Add(col);
                        camoW.Add(new Vector2(0f, camoRegion ? 1f : 0f));
                        wear.Add(vw);
                        pattern.Add(world[i] * PatternScale + new Vector3(0.37f, 0.11f, 0.53f));
                    }
                    tris.Add(n);
                }
                report.AppendLine($"  '{region}': {(camoRegion ? "camouflage" : "colour " + c)} ({t.Length / 3} tris)");
            }
            var mesh = new Mesh { name = src.name + " (coloured)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = src.bindposes;
            mesh.SetColors(colours);
            mesh.SetUVs(1, wear);
            mesh.SetUVs(2, camoW);
            mesh.SetUVs(3, pattern);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh;
            var mat = new Material(ArtMaterials.Stylized);
            if (camo != null) mat.SetTexture("_CamoTex", camo);
            mat.SetFloat("_CamoShade", 1f);
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            return report.ToString();
        }

        // ------------------------------------------------------------------ grips

        /// <summary>A hand's frame at rest (T-pose): finger direction, index-side direction, palm direction.</summary>
        private class HandRig
        {
            public Transform Shoulder, Upper, Lower, Hand;
            public Transform[][] Fingers;   // Thumb, Index, Middle, Ring, Little: three bones each
            public Quaternion HandRest;
            public Vector3 F0, T0, N0;
            public float Handed;            // n = Handed * cross(t, f)
            public Vector3 PalmFromWrist;   // the palm's surface centre, from the wrist, at rest (world)
            public Vector3 KnucklesFromWrist; // the palm side of the middle knuckle, from the wrist, at rest (world)
        }

        private static HandRig Rig(Transform[] bones, string s)
        {
            Transform B(string n) => bones.FirstOrDefault(b => b != null && b.name == n);
            var r = new HandRig
            {
                Shoulder = B($"Shoulder.{s}"), Upper = B($"UpperArm.{s}"), Lower = B($"LowerArm.{s}"), Hand = B($"Hand.{s}"),
                Fingers = new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Select(f => new[] { B($"{f}1.{s}"), B($"{f}2.{s}"), B($"{f}3.{s}") }).ToArray(),
            };
            r.HandRest = r.Hand.rotation;
            var wrist = r.Hand.position;
            var middle = r.Fingers[2][0].position;
            r.F0 = (middle - wrist).normalized;
            var across = (r.Fingers[1][0].position - r.Fingers[4][0].position).normalized;   // little → index
            r.T0 = (across - Vector3.Dot(across, r.F0) * r.F0).normalized;
            // In the T-pose the palms face down.
            r.N0 = Vector3.down;
            r.N0 = (r.N0 - Vector3.Dot(r.N0, r.F0) * r.F0 - Vector3.Dot(r.N0, r.T0) * r.T0).normalized;
            r.Handed = Mathf.Sign(Vector3.Dot(r.N0, Vector3.Cross(r.T0, r.F0)));
            // Palm surface: 60 % of the way to the knuckles, a little to the palm side of the bones.
            var palmThickness = (middle - wrist).magnitude * 0.22f;
            r.PalmFromWrist = (middle - wrist) * 0.6f + r.N0 * palmThickness;
            r.KnucklesFromWrist = (middle - wrist) + r.N0 * palmThickness * 0.6f;
            return r;
        }

        private static Quaternion Basis(Vector3 f, Vector3 t, Vector3 n) => Quaternion.LookRotation(f, n);

        /// <summary>
        /// Puts the hand so its palm faces <paramref name="n"/> with its index side towards <paramref name="t"/>, the palm
        /// surface at <paramref name="palmAt"/>: returns the wrist position and sets the hand's rotation afterwards via
        /// <see cref="Orient"/>.
        /// </summary>
        private static (Vector3 wrist, Quaternion delta) Place(HandRig h, Vector3 palmAt, Vector3 t, Vector3 n)
        {
            n.Normalize();
            t = (t - Vector3.Dot(t, n) * n).normalized;
            var f = (h.Handed * Vector3.Cross(n, t)).normalized;
            var delta = Basis(f, t, n) * Quaternion.Inverse(Basis(h.F0, h.T0, h.N0));
            return (palmAt - delta * h.PalmFromWrist, delta);
        }

        /// <summary>As <see cref="Place"/>, but puts the palm side of the knuckles at <paramref name="knucklesAt"/>.</summary>
        private static (Vector3 wrist, Quaternion delta) PlaceKnuckles(HandRig h, Vector3 knucklesAt, Vector3 t, Vector3 n)
        {
            n.Normalize();
            t = (t - Vector3.Dot(t, n) * n).normalized;
            var f = (h.Handed * Vector3.Cross(n, t)).normalized;
            var delta = Basis(f, t, n) * Quaternion.Inverse(Basis(h.F0, h.T0, h.N0));
            return (knucklesAt - delta * h.KnucklesFromWrist, delta);
        }

        /// <summary>
        /// The front edge of a grip on one side: where the knuckles go so the fingers wrap round the front. From the real
        /// surfaces: the side (a ray from <paramref name="side"/>) and the front (a ray from <paramref name="front"/>).
        /// </summary>
        private static Vector3 FrontEdge(Vector3 centre, Vector3 side, Vector3 front)
        {
            side.Normalize();
            front.Normalize();
            var s = Surface(centre, -side);
            var fr = Surface(centre, -front);
            return centre + side * (Vector3.Dot(s - centre, side) + FingerRadius) + front * (Vector3.Dot(fr - centre, front) + FingerRadius * 0.5f);
        }

        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            var current = child.position - bone.position;
            var wanted = target - bone.position;
            if (current.sqrMagnitude < 1e-8f || wanted.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(current, wanted) * bone.rotation;
        }

        /// <summary>Collarbone part-way towards the target, then a two-bone reach for the wrist; then the hand's own turn.</summary>
        /// <summary>
        /// Puts a hand on a grip and solves its arm together (owner, 2026-10-06: the elbow too low and the arm splayed out
        /// looked broken). The hand may turn round the grip's axis (as a real hand does) by up to ±35°, and for each turn
        /// the elbow is solved; the combination kept has the straightest wrist with the elbow no further out than
        /// <paramref name="outMax"/> and no lower than <paramref name="dropMax"/> below the shoulder, near the hold's
        /// standard elbow direction <paramref name="pole"/>.
        /// </summary>
        private static void ReachGrip(HandRig h, Vector3 knucklesAt, Vector3 t, Vector3 n, Vector3 pole, float outMax, float dropMax, bool collarbone = true)
        {
            var best = SolveArm(h, knucklesAt, t, n, pole, outMax, dropMax, 5f, 96);
            var a = (h.Lower.position - h.Upper.position).magnitude;
            var b = (h.Hand.position - h.Lower.position).magnitude;
            if (h.Shoulder != null && collarbone)
            {
                var cur = h.Upper.position - h.Shoulder.position;
                var towards = (best.wrist - h.Shoulder.position).normalized * cur.magnitude;
                // The collarbone follows the arm a little: 30 % of the way, at most 10° (a reach across the body would
                // otherwise swing the shoulder joint forward and up after the elbow was chosen).
                var full = Quaternion.FromToRotation(cur, towards);
                full.ToAngleAxis(out var fullAngle, out _);
                var part = Mathf.Min(0.3f, fullAngle > 0.01f ? 10f / fullAngle : 0.3f);
                h.Shoulder.rotation = Quaternion.Slerp(Quaternion.identity, full, part) * h.Shoulder.rotation;
            }
            // The shoulder moved a little: re-solve the elbow on the new circle nearest the chosen one.
            var s2 = h.Upper.position;
            var d2 = best.wrist - s2;
            var dist2 = Mathf.Clamp(d2.magnitude, 0.01f, (a + b) * 0.999f);
            var dir2 = d2.normalized;
            var ang2 = Mathf.Acos(Mathf.Clamp((a * a + dist2 * dist2 - b * b) / (2f * a * dist2), -1f, 1f));
            var c2 = s2 + dir2 * (a * Mathf.Cos(ang2));
            var off = best.elbow - c2;
            off -= Vector3.Dot(off, dir2) * dir2;
            var elbow = c2 + off.normalized * (a * Mathf.Sin(ang2));
            Aim(h.Upper, h.Lower, elbow);
            Aim(h.Lower, h.Hand, s2 + dir2 * dist2);
            h.Hand.rotation = best.delta * h.HandRest;
            var bendNow = Vector3.Angle((h.Hand.position - h.Lower.position).normalized, (best.delta * h.F0).normalized);
            Diag.AppendLine($"  {h.Hand.name}: wrist {(h.Hand.position - best.wrist).magnitude * 100f:F1} cm from its target, wrist bend {bendNow:0}°, elbow {(s2.y - elbow.y) * 100f:0} cm below the shoulder, {(elbow.x - s2.x) * Mathf.Sign(s2.x) * 100f:0} cm outwards");
        }

        private static void Reach(HandRig h, Vector3 wrist, Quaternion delta, Vector3 pole, float poleWeight = 0f)
        {
            if (h.Shoulder != null)
            {
                var cur = h.Upper.position - h.Shoulder.position;
                var towards = (wrist - h.Shoulder.position).normalized * cur.magnitude;
                h.Shoulder.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, towards), 0.3f) * h.Shoulder.rotation;
            }
            var a = (h.Lower.position - h.Upper.position).magnitude;
            var b = (h.Hand.position - h.Lower.position).magnitude;
            var s = h.Upper.position;
            var d = wrist - s;
            var dist = Mathf.Clamp(d.magnitude, 0.01f, (a + b) * 0.999f);
            var dir = d.normalized;
            var angle = Mathf.Acos(Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f));
            // Every elbow position that joins the shoulder and the wrist lies on a circle round the shoulder–wrist line.
            // Owner, 2026-10-06: a low, splayed elbow made the arm look broken. In a real grip the wrist stays nearly
            // straight: the forearm continues the line of the hand. So the elbow is the point on that circle nearest to
            // where the hand's line leads (behind the wrist by a forearm's length), with a light preference for hanging
            // down rather than up, never inside the body.
            var handAxis = (delta * h.F0).normalized;
            var ideal = wrist - handAxis * b;
            var centre = s + dir * (a * Mathf.Cos(angle));
            var ring = a * Mathf.Sin(angle);
            var u = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(dir, u).normalized;
            var elbow = centre + u * ring;
            var bestScore = float.MaxValue;
            var side = Mathf.Sign(s.x);
            for (var k = 0; k < 144; k++)
            {
                var th = k / 144f * Mathf.PI * 2f;
                var e = centre + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * ring;
                // With a pole weight, the hold's standard elbow direction (rifle: firing elbow out to the side a little
                // below the shoulder, support elbow down under the handguard) counts too.
                var preferred = s + pole.normalized * a;
                var score = (e - ideal).sqrMagnitude + Mathf.Max(0f, e.y - s.y) * 0.3f + poleWeight * (e - preferred).sqrMagnitude;
                // Not inside the torso: the elbow stays outside the body's own side.
                if (e.x * side < 0.14f && e.z > -0.2f && e.z < 0.22f && e.y > 0.6f) score += 10f;
                if (score < bestScore)
                {
                    bestScore = score;
                    elbow = e;
                }
            }
            Aim(h.Upper, h.Lower, elbow);
            Aim(h.Lower, h.Hand, s + dir * dist);
            h.Hand.rotation = delta * h.HandRest;
            var bend = Vector3.Angle((h.Hand.position - h.Lower.position).normalized, handAxis);
            Diag.AppendLine($"  {h.Hand.name}: wrist {(h.Hand.position - wrist).magnitude * 100f:F1} cm from its target, wrist bend {bend:0}°, elbow {(elbow.y - s.y) * 100f:0} cm from shoulder height, {(elbow.x - s.x) * side * 100f:0} cm outwards");
        }

        /// <summary>
        /// Wraps a finger round a grip of <paramref name="radius"/> (to the finger's centre line): each joint bends by
        /// its segment's length over the radius (the angle it subtends on that circle), so the finger follows the grip's
        /// surface; capped by the max angles (an index finger on the trigger, a thumb).
        /// </summary>
        private static void Wrap(HandRig h, int finger, float radius, float max1 = 100f, float max2 = 100f, float max3 = 85f)
        {
            var b = h.Fingers[finger];
            if (b.Any(x => x == null)) return;
            var l1 = (b[1].position - b[0].position).magnitude;
            var l2 = (b[2].position - b[1].position).magnitude;
            var l3 = l2 * 0.9f;
            float A(float l, float max) => Mathf.Min(max, l / radius * Mathf.Rad2Deg);
            // Half of the first bend at the knuckle (the palm already curves into the grip there).
            var keep = _gun;
            _gun = null;
            Curl(h, finger, A(l1, max1) * 0.6f + 20f, A(l2, max2), A(l3, max3));
            Diag.AppendLine($"    wrap {h.Fingers[finger][0].name}: radius {radius * 100f:F1} cm, bends {A(l1, max1) * 0.6f + 20f:0}/{A(l2, max2):0}/{A(l3, max3):0}°");
            _gun = keep;
        }

        /// <summary>
        /// A grip's real cross-section from the weapon mesh (model units): the vertices in a thin slice across the grip at
        /// <paramref name="c"/> (along <paramref name="axis"/>), within <paramref name="reach"/> of it so the magazine or
        /// trigger guard nearby don't count. Returns how far the front, the back and the side reach from the centre line.
        /// </summary>
        private static (float front, float back, float half, Vector2 mid) Section(string id, Vector2 c, Vector2 axis, float reach, float zc = 0f)
        {
            var mesh = PackArt.Model(id);
            axis.Normalize();
            var fwd = new Vector2(axis.y, -axis.x);   // across the grip, towards the muzzle
            if (fwd.x < 0f) fwd = -fwd;
            float front = 0f, back = 0f, half = 0f;
            foreach (var v in mesh.vertices)
            {
                var d = new Vector2(v.x, v.y) - c;
                if (Mathf.Abs(Vector2.Dot(d, axis)) > 0.025f) continue;
                var across = Vector2.Dot(d, fwd);
                if (Mathf.Abs(across) > reach) continue;
                front = Mathf.Max(front, across);
                back = Mathf.Min(back, across);
                half = Mathf.Max(half, Mathf.Abs(v.z - zc));
            }
            // The grip's own centre line sits midway between its front and back.
            return (front, back, half, c + fwd * (front + back) * 0.5f);
        }

        /// <summary>A grip in the world: its centre, its forward (across) direction, the front-edge corner on a side, its radius.</summary>
        private static (Vector3 centre, Vector3 edge, float radius) Grip(GameObject gun, string id, Vector2 c, Vector2 axis, float reach, float sideSign, bool log = true, float zc = 0f)
        {
            var (front, back, half, mid) = Section(id, c, axis, reach, zc);
            var t = gun.transform;
            axis.Normalize();
            var fwd2 = new Vector2(axis.y, -axis.x);
            if (fwd2.x < 0f) fwd2 = -fwd2;
            var depth = (front - back) * 0.5f;
            var centre = t.TransformPoint(new Vector3(mid.x, mid.y, zc));
            // The knuckles: at the front edge on the chosen side (+1 the gun's right, -z in model space), a finger's
            // thickness out from the surface.
            var edgeModel = new Vector3(mid.x + fwd2.x * depth, mid.y + fwd2.y * depth, zc - sideSign * half);
            var edge = t.TransformPoint(edgeModel);
            var outward = (t.TransformPoint(edgeModel) - t.TransformPoint(new Vector3(edgeModel.x, edgeModel.y, zc))).normalized;
            edge += outward * FingerRadius + (t.TransformPoint(new Vector3(mid.x + fwd2.x, mid.y + fwd2.y, 0f)) - t.TransformPoint(new Vector3(mid.x, mid.y, 0f))).normalized * FingerRadius * 0.5f;
            // Radius round the grip to the fingers' centre line: the average of half its depth and half its width, in metres.
            var depthW = (t.TransformPoint(new Vector3(mid.x + fwd2.x * depth, mid.y + fwd2.y * depth, zc)) - centre).magnitude;
            var halfW = (t.TransformPoint(new Vector3(mid.x, mid.y, zc + half)) - centre).magnitude;
            if (log) Diag.AppendLine($"    grip {id} at ({c.x:F2},{c.y:F2}): depth {depthW * 200f:F1} cm, width {halfW * 200f:F1} cm");
            return (centre, edge, (depthW + halfW) * 0.5f + FingerRadius);
        }

        /// <summary>The grip's radius around a point: half its width (from the side) and depth (from the front), averaged.</summary>
        private static float GripRadius(Vector3 centre, Vector3 side, Vector3 front)
        {
            var w = (Surface(centre, -side) - centre).magnitude;
            var d = (Surface(centre, -front) - centre).magnitude;
            return (w + d) * 0.5f + FingerRadius;
        }

        /// <summary>
        /// Curls a finger: each joint turns towards the palm by its angle (degrees), about the knuckle line. The hand
        /// must already be placed.
        /// </summary>
        private static void Curl(HandRig h, int finger, float a1, float a2, float a3)
        {
            var bones = h.Fingers[finger];
            if (bones.Any(b => b == null)) return;
            // The current palm direction and knuckle line, from the placed hand.
            var palm = (h.Hand.rotation * Quaternion.Inverse(h.HandRest)) * h.N0;
            var knuckles = (h.Hand.rotation * Quaternion.Inverse(h.HandRest)) * h.T0;
            var angles = new[] { a1, a2, a3 };
            for (var j = 0; j < 3; j++)
            {
                var next = j < 2 ? bones[j + 1].position : bones[j].position + (bones[j].position - bones[j - 1].position);
                var dir = (next - bones[j].position).normalized;
                // Turn about the knuckle line, in whichever sense brings the finger towards the palm.
                var axis = finger == 0 ? Vector3.Cross(dir, palm).normalized : knuckles;
                var plus = Quaternion.AngleAxis(angles[j], axis) * dir;
                var sign = Vector3.Dot(plus, palm) >= Vector3.Dot(Quaternion.AngleAxis(-angles[j], axis) * dir, palm) ? 1f : -1f;
                if (_gun == null)
                {
                    bones[j].rotation = Quaternion.AngleAxis(sign * angles[j], axis) * bones[j].rotation;
                    continue;
                }
                // With a weapon: close the joint a few degrees at a time, up to its angle, and stop when the finger
                // (from this joint to the tip) would go into the weapon. The finger then lies on its surface.
                for (var a = 0f; a < angles[j]; a += 3f)
                {
                    var before = bones[j].rotation;
                    bones[j].rotation = Quaternion.AngleAxis(sign * 3f, axis) * bones[j].rotation;
                    if (Touches(bones, j))
                    {
                        bones[j].rotation = before;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Closes a finger round the weapon by contact: each joint bends (up to its limit) until the finger meets the
        /// surface, so it lies on the gun whatever the part's size. For fingers that start outside the gun.
        /// </summary>
        private static void Grasp(HandRig h, int finger, float max1 = 100f, float max2 = 100f, float max3 = 85f)
        {
            var bones = h.Fingers[finger];
            if (_gun == null || bones.Any(b => b == null)) return;
            var rest = bones.Select(b => b.localRotation).ToArray();
            var keep = _gun;
            // The finger's shape as it closes: the joints bend together (the knuckle a little less), scaled by k.
            void Shape(float k)
            {
                for (var j = 0; j < 3; j++) bones[j].localRotation = rest[j];
                _gun = null;
                Curl(h, finger, Mathf.Min(max1, 70f * k), Mathf.Min(max2, 90f * k), Mathf.Min(max3, 75f * k));
                _gun = keep;
            }
            bool Inside()
            {
                for (var j = 0; j < 3; j++)
                {
                    var a = bones[j].position;
                    var b = j < 2 ? bones[j + 1].position : bones[2].position + (bones[2].position - bones[1].position) * 0.9f;
                    if (Physics.CheckSphere((a + b) * 0.5f, FingerRadius * 0.5f) || Physics.CheckSphere(b, FingerRadius * 0.5f)) return true;
                }
                return false;
            }
            // Close step by step; keep the last shape that doesn't go into the gun (the finger then lies on it).
            var best = 0.15f;
            for (var k = 0.15f; k <= 1.6f; k += 0.05f)
            {
                Shape(k);
                if (Inside()) break;
                best = k;
            }
            Shape(best);
            Diag.AppendLine($"    grasp {bones[0].name}: closed to {best:F2} (knuckle {Mathf.Min(max1, 70f * best):0}°)");
        }

/// <summary>
        /// The arm solve behind <see cref="ReachGrip"/>, without moving anything: over the hand's roll round the grip
        /// (±35°, every <paramref name="rollStep"/>°) and <paramref name="ring"/> elbow places on the IK circle, the one
        /// with the straightest wrist within the elbow limits. Returns its score, wrist, hand turn, elbow, wrist bend
        /// (degrees) and how far the elbow is below the shoulder.
        /// </summary>
        private static (float score, Vector3 wrist, Quaternion delta, Vector3 elbow, float bend, float drop) SolveArm(HandRig h, Vector3 knucklesAt, Vector3 t, Vector3 n, Vector3 pole, float outMax, float dropMax, float rollStep, int ring)
        {
            var s = h.Upper.position;
            var side = Mathf.Sign(s.x);
            var a = (h.Lower.position - h.Upper.position).magnitude;
            var b = (h.Hand.position - h.Lower.position).magnitude;
            var axis = t.normalized;
            (float score, Vector3 wrist, Quaternion delta, Vector3 elbow, float bend, float drop) best = (float.MaxValue, default, default, default, 0f, 0f);
            for (var roll = -35f; roll <= 35.01f; roll += rollStep)
            {
                var turn = Quaternion.AngleAxis(roll, axis);
                var (wrist, delta) = PlaceKnuckles(h, knucklesAt, axis, turn * n);
                var d = wrist - s;
                var dist = Mathf.Clamp(d.magnitude, 0.01f, (a + b) * 0.999f);
                var dir = d.normalized;
                var ang = Mathf.Acos(Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f));
                var centre = s + dir * (a * Mathf.Cos(ang));
                var radius = a * Mathf.Sin(ang);
                var u = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
                var v = Vector3.Cross(dir, u).normalized;
                var handAxis = (delta * h.F0).normalized;
                var preferred = s + pole.normalized * a;
                for (var k = 0; k < ring; k++)
                {
                    var th = k / (float)ring * Mathf.PI * 2f;
                    var e = centre + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * radius;
                    var bend = Vector3.Angle((wrist - e).normalized, handAxis);
                    var outward = (e.x - s.x) * side;
                    var drop = s.y - e.y;
                    var score = bend * bend * 0.002f + (e - preferred).sqrMagnitude * 2f
                              + Mathf.Max(0f, outward - outMax) * 40f + Mathf.Max(0f, drop - dropMax) * 40f
                              // Elbows stay below the shoulders in every reference hold (owner, 2026-10-06: the
                              // shotgun elbow was raised too high): at least 5 cm below, raising it costs far more than a bent wrist.
                              + Mathf.Max(0f, 0.05f - drop) * 150f + Mathf.Max(0f, -outward - 0.03f) * 40f + Mathf.Abs(roll) * 0.004f;
                    if (LimbInBody(s, e, wrist, knucklesAt)) score += 100f;
                    if (score < best.score) best = (score, wrist, delta, e, bend, drop);
                }
            }
            return best;
        }

/// <summary>
        /// The character's own body (owner, 2026-10-06: character 03's bulky chest swallowed the guns): for every 3 cm
        /// of height, the extent of the T-pose mesh between the two shoulder joints (torso, head, legs), at game size.
        /// Replaces a fixed box measured from character 02.
        /// </summary>
        private static Dictionary<(int band, int col), (float z0, float z1)> _torso = new Dictionary<(int, int), (float, float)>();
        private const float TorsoBand = 0.03f;
        private const float TorsoColumn = 0.02f;

        /// <summary>Diagnosis only: measure the torso as the current previews were (by skin weights) to reproduce them.</summary>
        public static bool LegacyTorso;

        /// <summary>The upper body's turn from the T-pose (BodyPose) about the waist joint; identity in the T-pose.</summary>
        private static Quaternion _upperRot = Quaternion.identity;
        private static Vector3 _waist;
        /// <summary>The arms' thickness (radius round the bone, metres), measured with the torso.</summary>
        private static float _limbRadius = 0.05f;

        /// <summary>The head seen from above (the game camera): its centre and radius on the ground plane.</summary>
        private static Vector2 _headCentre;
        private static float _headRadius;

        private static void MeasureTorso(SkinnedMeshRenderer smr)
        {
            _torso.Clear();
            _upperRot = Quaternion.identity;
            {
                var neck = smr.bones.First(b => b != null && b.name == "Neck").position;
                var hb = new Mesh();
                smr.BakeMesh(hb, true);
                var head = hb.vertices.Select(v => smr.transform.TransformPoint(v)).Where(p => p.y > neck.y + 0.05f).ToList();
                Object.DestroyImmediate(hb);
                _headCentre = new Vector2(head.Average(p => p.x), head.Average(p => p.z));
                _headRadius = head.Max(p => (new Vector2(p.x, p.z) - _headCentre).magnitude) * 0.9f;
            }
            {
                // Arm thickness: the distance of the forearm's vertices from its bone line, 80th percentile.
                var lo = smr.bones.First(b => b != null && b.name == "LowerArm.R").position;
                var ha = smr.bones.First(b => b != null && b.name == "Hand.R").position;
                var mb = new Mesh();
                smr.BakeMesh(mb, true);
                var d = mb.vertices.Select(v => smr.transform.TransformPoint(v)).Where(p => p.x > lo.x && p.x < ha.x)
                    .Select(p => Vector3.Cross(ha - lo, p - lo).magnitude / (ha - lo).magnitude).OrderBy(x => x).ToList();
                if (d.Count > 10) _limbRadius = d[(int)(d.Count * 0.8f)];
                Object.DestroyImmediate(mb);
            }
            if (LegacyTorso)
            {
                var core = new HashSet<string> { "Hips", "Spine", "Chest", "Neck", "Head" };
                var lb = new Mesh();
                smr.BakeMesh(lb, true);
                var lw = smr.sharedMesh.boneWeights;
                var lv = lb.vertices;
                var box = new Dictionary<int, (float x0, float x1, float z0, float z1)>();
                for (var i = 0; i < lv.Length; i++)
                {
                    var bone = smr.bones[lw[i].boneIndex0];
                    if (bone == null || !core.Contains(bone.name)) continue;
                    var p = smr.transform.TransformPoint(lv[i]);
                    var k = Mathf.FloorToInt(p.y / TorsoBand);
                    box[k] = box.TryGetValue(k, out var e) ? (Mathf.Min(e.x0, p.x), Mathf.Max(e.x1, p.x), Mathf.Min(e.z0, p.z), Mathf.Max(e.z1, p.z)) : (p.x, p.x, p.z, p.z);
                }
                // As one column per band covering its box.
                foreach (var kv in box)
                    for (var c = Mathf.FloorToInt(kv.Value.x0 / TorsoColumn); c <= Mathf.FloorToInt(kv.Value.x1 / TorsoColumn); c++)
                        _torso[(kv.Key, c)] = (kv.Value.z0, kv.Value.z1);
                Object.DestroyImmediate(lb);
                return;
            }
            // By shape, not by skin weights (owner, 2026-10-06: in these rigs most of the chest is skinned to the
            // shoulder and upper-arm bones and the belly to the legs, so weights missed most of the body). In the
            // T-pose the arms reach out sideways, so everything between the two shoulder joints is the body.
            // The shoulder caps (the outer 15 % up to the shoulder joints) go with the arms when they move, so they're
            // left out: that's where a stock clamped under the arm sits.
            var shoulderX = 0.85f * smr.bones.Where(b => b != null && b.name.StartsWith("UpperArm.")).Select(b => Mathf.Abs(b.position.x)).DefaultIfEmpty(0.3f).Min();
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var v = baked.vertices;
            // Characters 03 and 04 (owner, 2026-10-06): a belly wider than the shoulders reaches past the shoulder
            // joints, so their profile is every vertex not skinned mainly to an arm bone, wherever it is. Character 02
            // keeps the shape-only profile her locked holds were solved with.
            var byBones = smr.gameObject.name != "chr_girl";
            var w = smr.sharedMesh.boneWeights;
            var armBone = smr.bones.Select(b => b != null && (b.name.StartsWith("UpperArm") || b.name.StartsWith("LowerArm") || b.name.StartsWith("Hand") || b.name.StartsWith("Thumb") || b.name.StartsWith("Index") || b.name.StartsWith("Middle") || b.name.StartsWith("Ring") || b.name.StartsWith("Little"))).ToArray();
            for (var i = 0; i < v.Length; i++)
            {
                var p = smr.transform.TransformPoint(v[i]);
                if (byBones ? armBone[w[i].boneIndex0] : Mathf.Abs(p.x) > shoulderX) continue;
                // Per 3 cm of height and 2 cm across, the body's front and back: a profile that follows its round shape.
                var key = (Mathf.FloorToInt(p.y / TorsoBand), Mathf.FloorToInt(p.x / TorsoColumn));
                _torso[key] = _torso.TryGetValue(key, out var e) ? (Mathf.Min(e.z0, p.z), Mathf.Max(e.z1, p.z)) : (p.z, p.z);
            }
            Object.DestroyImmediate(baked);
        }

        /// <summary>Is the point inside the character's torso or head (within <paramref name="margin"/> of it)?</summary>
        private static bool InTorso(Vector3 p, float margin)
        {
            // The profile was measured in the T-pose; with the upper body turned and leaning (BodyPose), a point is
            // first carried back into the T-pose: fully above the waist, blending in over 12 cm (as the skin does).
            if (_upperRot != Quaternion.identity)
            {
                var w = Mathf.Clamp01((p.y - _waist.y) / 0.12f);
                p = _waist + Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(_upperRot), w) * (p - _waist);
            }
            var k = Mathf.FloorToInt(p.y / TorsoBand);
            var c0 = Mathf.FloorToInt((p.x - margin) / TorsoColumn);
            var c1 = Mathf.FloorToInt((p.x + margin) / TorsoColumn);
            var dk = Mathf.CeilToInt(margin / TorsoBand);
            for (var b = k - dk; b <= k + dk; b++)
            for (var c = c0; c <= c1; c++)
            {
                if (!_torso.TryGetValue((b, c), out var e)) continue;
                if (p.z > e.z0 - margin && p.z < e.z1 + margin) return true;
            }
            return false;
        }

        /// <summary>
        /// Does the arm (shoulder joint, elbow, wrist, knuckles; as thick as the character's arm) pass into the body?
        /// The upper arm is tested from 60 % of its length: nearer the shoulder it joins the body at the armpit.
        /// </summary>
        /// <summary>
        /// The posed body (arms left out) as a collider on the Ignore Raycast layer, while a full-body hold is searched:
        /// the default queries (finger contact) don't see it; the arm tests ask for it with <see cref="BodyMask"/>.
        /// </summary>
        private static MeshCollider _bodyCol;
        private const int BodyLayer = 2;
        private const int BodyMask = 1 << BodyLayer;

/// <summary>
        /// How much of the gun's front half the head hides from the game camera above (metres of overlap summed over
        /// samples): the chibi references show the gun's silhouette beside the head, not under it.
        /// </summary>
        private static float HiddenUnderHead(System.Func<Vector3, Vector3> world, LongGunSpec g)
        {
            var hidden = 0f;
            for (var k = 0; k <= 5; k++)
            {
                var p = world(new Vector3(k * 0.1f, g.Butt.y, g.Zc));
                hidden += Mathf.Max(0f, _headRadius - (new Vector2(p.x, p.z) - _headCentre).magnitude);
            }
            return hidden;
        }

        /// <summary>Is the posed body within <paramref name="r"/> of the point (or the point inside it)?</summary>
        private static bool BodyNear(Vector3 p, float r) => _bodyCol != null
            ? Physics.CheckSphere(p, r, BodyMask, QueryTriggerInteraction.Ignore) || InTorso(p, 0f)
            : InTorso(p, r);

        /// <summary>Diagnosis: where along the arm LimbInBody found the body (upper arm 40-100 %, forearm 10-90 %, hand).</summary>
        public static readonly int[] LimbHits = new int[12];

        private static bool LimbInBody(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 knuckles)
        {
            // 70 % of the arm's radius (the box test over-reaches at the corners); the real-mesh check decides.
            // From half way down the upper arm (nearer the shoulder it joins the body at the armpit), the forearm and
            // the hand. A point clashes when the arm's core (half its radius) meets the posed body's surface, or the
            // point lies inside the body.
            bool HitStill(Vector3 p) => _bodyCol != null
                ? Physics.CheckSphere(p, _limbRadius * 0.5f, BodyMask, QueryTriggerInteraction.Ignore) || InTorso(p, 0f)
                : InTorso(p, _limbRadius * 0.7f);
            // The waist sway an animation adds (owner, option 1: at most ±5°) is judged by the real-mesh verification.
            bool Hit(Vector3 p) => HitStill(p);
            for (var k = 3; k <= 10; k++)
            {
                var p = Vector3.Lerp(shoulder, elbow, k / 10f);
                var room = _limbRadius * 0.5f + 0.015f;
                if (k >= 5 && (Hit(p) || _bodyCol != null && Physics.CheckSphere(p, room, BodyMask, QueryTriggerInteraction.Ignore))) { LimbHits[0]++; return true; }
            }
            for (var k = 1; k <= 9; k += 2)
                if (Hit(Vector3.Lerp(elbow, wrist, k / 10f))) { LimbHits[4 + (k - 1) / 2]++; return true; }
            for (var k = 0; k <= 2; k++)
                if (Hit(Vector3.Lerp(wrist, knuckles, k / 2f))) { LimbHits[9 + k]++; return true; }
            return false;
        }

        /// <summary>The weapon being held (with a collider) while posing; fingers stop on it.</summary>
        private static Collider _gun;
        private const float FingerRadius = 0.017f;

        /// <summary>Does the finger, from joint <paramref name="j"/> to its tip, go into the weapon?</summary>
        private static bool Touches(Transform[] bones, int j)
        {
            for (var k = j + 1; k <= 3; k++)
            {
                var p = k < 3 ? bones[k].position : bones[2].position + (bones[2].position - bones[1].position) * 0.9f;
                var from = k == j + 1 ? bones[j].position : (k < 3 ? bones[k - 1].position : bones[2].position);
                // The segment's middle and its end.
                // Resting on the surface is fine; only going into it stops the joint.
                if (Physics.CheckSphere((from + p) * 0.5f, FingerRadius * 0.7f) || Physics.CheckSphere(p, FingerRadius * 0.7f)) return true;
            }
            return false;
        }

        /// <summary>The weapon's surface: a ray from outside, along <paramref name="dir"/>, towards <paramref name="point"/>.</summary>
        private static Vector3 Surface(Vector3 point, Vector3 dir)
        {
            dir.Normalize();
            if (_gun != null && _gun.Raycast(new Ray(point - dir * 0.5f, dir), out var hit, 1f)) return hit.point;
            return point - dir * 0.05f;
        }

        public enum Hold { Rifle, Pistol, Shotgun, Sniper }

        // ------------------------------------------------------------------ longer arms (owner, 2026-10-06: option B)

        /// <summary>
        /// Lengthens the character's arms by <paramref name="k"/> without touching the source model: the upper arm and
        /// forearm stretch along the arm (T-pose, so along a straight line from shoulder joint to wrist), the hand and
        /// fingers move out unchanged, the shoulder and body stay. The elbow, wrist and finger joints move to match and
        /// the bind poses are rebuilt, so the skin follows exactly. The character must be in its rest pose.
        /// </summary>
        public static void LengthenArms(SkinnedMeshRenderer smr, float k)
        {
            if (Mathf.Abs(k - 1f) < 0.001f) return;
            var mesh = Object.Instantiate(smr.sharedMesh);
            mesh.name = smr.sharedMesh.name;
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var t = smr.transform;
            foreach (var s in new[] { "L", "R" })
            {
                Transform B(string n) => smr.bones.FirstOrDefault(b => b != null && b.name == n);
                var upper = B($"UpperArm.{s}");
                var lower = B($"LowerArm.{s}");
                var hand = B($"Hand.{s}");
                // In the mesh's own space.
                var u = t.InverseTransformPoint(upper.position);
                var h = t.InverseTransformPoint(hand.position);
                var dir = (h - u).normalized;
                var length = (h - u).magnitude;
                var armBones = new HashSet<int>();
                for (var i = 0; i < smr.bones.Length; i++)
                {
                    var b = smr.bones[i];
                    if (b == null) continue;
                    if (b == upper || b.IsChildOf(upper)) armBones.Add(i);
                }
                for (var v = 0; v < verts.Length; v++)
                {
                    var w = weights[v];
                    var onArm = (armBones.Contains(w.boneIndex0) ? w.weight0 : 0f) + (armBones.Contains(w.boneIndex1) ? w.weight1 : 0f)
                              + (armBones.Contains(w.boneIndex2) ? w.weight2 : 0f) + (armBones.Contains(w.boneIndex3) ? w.weight3 : 0f);
                    if (onArm <= 0f) continue;
                    var along = Vector3.Dot(verts[v] - u, dir);
                    if (along <= 0f) continue;
                    var push = Mathf.Min(along, length) * (k - 1f);
                    verts[v] += dir * push;
                }
                // Joints: the elbow out by its share, the wrist (and the hand and fingers with it) by the full amount.
                var worldDir = t.TransformDirection(dir).normalized;
                var lowerAlong = Vector3.Dot(t.InverseTransformPoint(lower.position) - u, dir);
                var scaleW = t.TransformVector(dir).magnitude;
                lower.position += worldDir * lowerAlong * (k - 1f) * scaleW;
                hand.position += worldDir * (length - lowerAlong) * (k - 1f) * scaleW;
            }
            mesh.vertices = verts;
            var bind = new Matrix4x4[smr.bones.Length];
            for (var i = 0; i < bind.Length; i++) bind[i] = smr.bones[i].worldToLocalMatrix * t.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh;
        }

        // ------------------------------------------------------------------ shouldered rifle hold

        /// <summary>The handguard's cross-section at model x (model units): its bottom, top and half width.</summary>
        private static (float bottom, float top, float half) Handguard(string id, float x)
        {
            var mesh = PackArt.Model(id);
            var bore = PackArt.MuzzleOf(id).y;
            float bottom = float.MaxValue, top = float.MinValue, half = 0f;
            foreach (var v in mesh.vertices)
            {
                if (Mathf.Abs(v.x - x) > 0.025f || v.y < bore - 0.09f || v.y > bore + 0.075f) continue;
                bottom = Mathf.Min(bottom, v.y);
                top = Mathf.Max(top, v.y);
                half = Mathf.Max(half, Mathf.Abs(v.z));
            }
            return (bottom, top, half);
        }

        /// <summary>
        /// The shouldered rifle (standard, after real technique): the stock butt in the right shoulder pocket, the barrel
        /// level or a touch down and a little inwards; the right hand closed on the pistol grip, index on the trigger;
        /// the support hand under the handguard, palm up, index towards the muzzle, fingers wrapping up its far side,
        /// as far forward as the arm reaches (60–70 % of the rifle). With <paramref name="apply"/> false it only reports
        /// how long the arms would need to be (<see cref="NeededArmScale"/>).
        /// </summary>
        private static bool Shouldered(HandRig right, HandRig left, GameObject gun, bool apply)
        {
            var gt = gun.transform;
            var scale = gt.localScale;
            var reachR = Reach(right) * 0.97f;
            var reachL = Reach(left) * 0.97f;
            var pocket = right.Upper.position + new Vector3(-0.07f, -0.07f, 0.05f);
            var bestScore = float.MaxValue;
            (float yaw, float pitch, float t) best = default;
            NeededArmScale = float.MaxValue;
            AttachCollider(gun);
            for (var yaw = -10f; yaw <= 0f; yaw += 2f)
            for (var pitch = 0f; pitch <= 8f; pitch += 2f)
            {
                gt.rotation = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(0f, -90f, 0f);
                gt.position = pocket - gt.rotation * Vector3.Scale(scale, new Vector3(-0.5f, 0.24f, 0f));
                Physics.SyncTransforms();
                var rg = Grip(gun, "ar_gun", new Vector2(-0.196f, 0.148f), new Vector2(0.35f, 0.94f), 0.06f, 1f, false);
                var gunRight = -gt.TransformDirection(Vector3.forward).normalized;
                var (rw, _) = PlaceKnuckles(right, rg.edge, gt.TransformDirection(new Vector3(0.35f, 0.94f, 0f)).normalized, -gunRight);
                var needR = (rw - right.Upper.position).magnitude / 0.97f / Reach(right);
                foreach (var tt in new[] { 0.7f, 0.66f, 0.62f, 0.58f })
                {
                    var (edge, _) = HandguardEdge(gun, tt);
                    var (lw, _) = PlaceKnuckles(left, edge, gt.TransformDirection(Vector3.right).normalized, gt.TransformDirection(Vector3.up).normalized);
                    var needL = (lw - left.Upper.position).magnitude / 0.97f / Reach(left);
                    if (Mathf.Approximately(tt, 0.62f)) NeededArmScale = Mathf.Min(NeededArmScale, Mathf.Max(needR, needL));
                    if (needR > 1f || needL > 1f) continue;
                    var score = Mathf.Abs(yaw) + pitch * 0.5f - tt * 20f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (yaw, pitch, tt);
                    }
                    break;
                }
            }
            if (!apply || bestScore == float.MaxValue) return false;
            gt.rotation = Quaternion.Euler(best.pitch, best.yaw, 0f) * Quaternion.Euler(0f, -90f, 0f);
            gt.position = pocket - gt.rotation * Vector3.Scale(scale, new Vector3(-0.5f, 0.24f, 0f));
            LastNote = $"rifle: shouldered, barrel {best.pitch:0}° down and {-best.yaw:0}° inwards, support hand on the handguard at {best.t * 100f:0} % of the rifle";
            AttachCollider(gun);
            var right2 = -gt.TransformDirection(Vector3.forward).normalized;
            var up = gt.TransformDirection(Vector3.up).normalized;
            var fwd = gt.TransformDirection(Vector3.right).normalized;
            var grip = Grip(gun, "ar_gun", new Vector2(-0.196f, 0.148f), new Vector2(0.35f, 0.94f), 0.06f, 1f);
            // Firing arm: elbow a little out and down, not splayed (real shouldered stance, compact).
            ReachGrip(right, grip.edge, gt.TransformDirection(new Vector3(0.35f, 0.94f, 0f)).normalized, -right2, new Vector3(0.45f, -0.85f, -0.05f), 0.3f, 0.45f);
            Wrap(right, 2, grip.radius);
            Wrap(right, 3, grip.radius);
            Wrap(right, 4, grip.radius);
            Wrap(right, 1, grip.radius, 40f, 55f, 35f);   // index on the trigger
            Wrap(right, 0, grip.radius, 35f, 40f, 30f);   // thumb round the far side
            Gaps(right);
            var (hgEdge, hgRadius) = HandguardEdge(gun, best.t);
            // Support arm: elbow down under the handguard, close in.
            ReachGrip(left, hgEdge, fwd, up, new Vector3(-0.2f, -0.9f, 0.4f), 0.15f, 0.45f);
            for (var f = 1; f <= 4; f++) Wrap(left, f, hgRadius);
            Wrap(left, 0, hgRadius, 30f, 30f, 20f);   // thumb along the near side
            Gaps(left);
            return true;
        }

        /// <summary>A long gun's hold points, in model units (+x the muzzle; Zc the model's centre plane across).</summary>
        private sealed class LongGunSpec
        {
            public string Id, Name;
            public Vector2 Butt, GripC, GripAxis;
            public float GripReach, Zc;
            /// <summary>A pistol grip (thumb round its far side) rather than the wrist of a straight stock.</summary>
            public bool PistolGrip;
            /// <summary>Support-hand parts, preferred first: palm up under the part anywhere from x0 to x1, its centre at y.</summary>
            public (string name, float x0, float x1, float y)[] Supports;
        }

        /// <summary>The AR (for the size table): pistol grip; support hand under the handguard.</summary>
        private static readonly LongGunSpec RifleSpec = new LongGunSpec
        {
            Id = "ar_gun", Name = "rifle", Butt = new Vector2(-0.5f, 0.24f), GripC = new Vector2(-0.196f, 0.148f),
            GripAxis = new Vector2(0.35f, 0.94f), GripReach = 0.06f, Zc = 0f, PistolGrip = true,
            Supports = new[] { ("handguard", 0.11f, 0.29f, 0.286f) },
        };

        /// <summary>
        /// Pump shotgun. References (shotgun fundamentals; USMC underarm / hip positions): the firing hand round the
        /// stock's wrist with the index on the trigger; the support hand on the pump (forend), which it must hold to
        /// cycle the gun, palm under, fingers round it. Measured: wrist x -0.215 y 0.10–0.23, trigger x -0.115, pump
        /// x 0.21–0.39 y 0.114–0.207, butt x -0.5.
        /// </summary>
        private static readonly LongGunSpec ShotgunSpec = new LongGunSpec
        {
            Id = "shotgun", Name = "shotgun", Butt = new Vector2(-0.5f, 0.12f), GripC = new Vector2(-0.205f, 0.17f),
            GripAxis = new Vector2(0.55f, 0.83f), GripReach = 0.07f, Zc = 0f, PistolGrip = false,
            Supports = new[] { ("pump", 0.23f, 0.37f, 0.16f) },
        };

        /// <summary>
        /// Bolt-action sniper with a thumbhole stock. References (standing / off-hand rifle position): the firing hand on
        /// the pistol grip, index on the trigger; the support hand palm up under the forend or the magazine with the
        /// rifle resting on it, the support elbow down against the body. Measured (the model's centre plane is z 0.043):
        /// grip x -0.19..-0.14 y 0.0–0.2, trigger x -0.1, magazine x -0.03..0.09 y 0.02–0.13, forend x 0.13–0.36.
        /// </summary>
        private static readonly LongGunSpec SniperSpec = new LongGunSpec
        {
            Id = "sniper_gun", Name = "sniper", Butt = new Vector2(-0.5f, 0.13f), GripC = new Vector2(-0.165f, 0.1f),
            GripAxis = new Vector2(0.25f, 0.97f), GripReach = 0.05f, Zc = 0.043f, PistolGrip = true,
            Supports = new[] { ("forend", 0.14f, 0.34f, 0.19f), ("magazine", -0.02f, 0.08f, 0.075f) },
        };

        /// <summary>
        /// Palm-up support under a part of the gun at model x, centred at model y: the knuckles at its bottom-right
        /// edge, the fingers wrapping up its right side. World edge and wrap radius. Rays, so low-poly parts work.
        /// </summary>
        private static (Vector3 edge, float radius) UnderEdge(GameObject gun, float x, float y, float zc)
        {
            var g = gun.transform;
            var centre = g.TransformPoint(new Vector3(x, y, zc));
            var up = g.TransformDirection(Vector3.up).normalized;
            var right = -g.TransformDirection(Vector3.forward).normalized;
            var bottom = Surface(centre, up);
            var side = Surface(centre, -right);
            var halfH = Vector3.Dot(centre - bottom, up);
            var halfW = Vector3.Dot(side - centre, right);
            var edge = centre - up * (halfH + FingerRadius * 0.5f) + right * (halfW + FingerRadius);
            return (edge, (halfH + halfW) * 0.5f + FingerRadius);
        }

        /// <summary>
        /// The underarm long-gun hold (USMC underarm assault / hip positions) for any long gun: the stock clamped under
        /// the firing arm against the side (its butt may reach behind the hip), the gun level and pointing ahead; the
        /// firing hand on the grip; the support hand on the most forward reference place the character's own arm
        /// reaches. Searched; returns false when no support place is reachable.
        /// </summary>
/// <summary>
        /// The chibi long-gun presentation (owner, 2026-10-06, option A; references: Brawl Stars character models):
        /// two hands, the gun raised to chest-to-shoulder height and held out on the firing side, pointing ahead; both
        /// elbows out from the body and below the shoulders, so the arms read fully; the support hand near the firing
        /// hand on the rear of the handguard / pump / forend.
        /// </summary>
        private static readonly Vector3 FiringPole = new Vector3(0.5f, -0.85f, 0f);
        private static readonly Vector3 SupportPole = new Vector3(-0.45f, -0.85f, 0.25f);
        /// <summary>How far out from the shoulder the firing elbow must be (m).</summary>
        private const float ElbowOut = 0.08f;

/// <summary>
        /// A pose recreated from a reference (owner, 2026-10-06): where the firing hand's grip is from the right
        /// shoulder (character facing +z, right +x), the gun's turn (yaw, + to the character's right) and tilt (pitch,
        /// + muzzle down), how far the search may go from them, and the body poses to try.
        /// </summary>
        public sealed class HoldTarget
        {
            public string Name;
            public Vector3 D, Span;
            public float Yaw, YawSpan, Pitch, PitchSpan;
            public float[] Twists, Leans, Rolls;
        }

        /// <summary>The reference target the long-gun search is recreating (null: the general search).</summary>
        public static HoldTarget Target;
        /// <summary>A locked hold's stored placement (null: search).</summary>
        public static (Vector3 d, float yaw, float pitch, int part, float x)? FixedPlacement;

        /// <summary>How far a placement is from the reference target (0 without one).</summary>
        private static float TargetCost(Vector3 d, float yaw, float pitch)
        {
            if (Target == null) return 0f;
            return (d - Target.D).magnitude * 40f + Mathf.Abs(yaw - Target.Yaw) * 0.4f + Mathf.Abs(pitch - Target.Pitch) * 0.4f;
        }

        /// <summary>The last solved placement: grip offset from the right shoulder, gun yaw and pitch, support part and x.</summary>
        public static (Vector3 d, float yaw, float pitch, int part, float x) LastPlacement;

        /// <summary>The last UnderarmLong's best score (lower is better; MaxValue when none).</summary>
        private static float LastHoldScore;

        private static bool UnderarmLong(HandRig right, HandRig left, GameObject gun, LongGunSpec g, bool apply, out string note, bool coarse = false)
        {
            // Coarse: twice the grid steps, no fine refinement (the full-body search tries many body poses).
            var st = coarse ? 2f : 1f;
            var gt = gun.transform;
            var scale = gt.localScale;
            var reachR = Reach(right) * 0.97f;
            // The support arm extended (the references: as far forward as it goes) but never locked.
            var reachL = Reach(left) * 0.985f;
            var armpit = right.Upper.position + new Vector3(-0.03f, -0.16f, -0.02f);
            AttachCollider(gun);
            // Hold points in model space, measured once; they move rigidly with the gun.
            gt.rotation = Quaternion.Euler(0f, -90f, 0f);
            gt.position = Vector3.zero;
            Physics.SyncTransforms();
            var parkedGrip = Grip(gun, g.Id, g.GripC, g.GripAxis, g.GripReach, 1f, false, g.Zc);
            var gripEdge = gt.InverseTransformPoint(parkedGrip.edge);
            var gripMid = gt.InverseTransformPoint(parkedGrip.centre);
            // The gripping hand wraps the grip all round: it needs the grip's radius plus two fingers' thickness of
            // room from the body (owner, 2026-10-06: the shotgun's firing hand sank into the hip).
            var handRoom = parkedGrip.radius + FingerRadius * 2f;
            // Support places: each part sampled along its length (the hand may sit anywhere on it), front first.
            var sup = new List<(int part, float x, Vector3 edge)>();
            var supRoom = new List<(Vector3 mid, float room)>();
            for (var pi = 0; pi < g.Supports.Length; pi++)
            {
                var p = g.Supports[pi];
                for (var k = 0; k < 5; k++)
                {
                    var x = Mathf.Lerp(p.x1, p.x0, k / 4f);
                    var ue = UnderEdge(gun, x, p.y, g.Zc);
                    sup.Add((pi, x, gt.InverseTransformPoint(ue.edge)));
                    supRoom.Add((new Vector3(x, p.y, g.Zc), ue.radius + FingerRadius * 2f));
                }
            }
            var gripAxis = new Vector3(g.GripAxis.x, g.GripAxis.y, 0f).normalized;
            var boreY = PackArt.MuzzleOf(g.Id).y;
            // Half the gun's thickness: its centre line keeps this far from the body.
            var gunHalf = 0.06f * scale.z;
            // The closest the wrist gets to the shoulder over the hand's roll round the grip (as ReachGrip allows).
            float WristDist(HandRig h, Vector3 knuckles, Vector3 t, Vector3 n)
            {
                var m = float.MaxValue;
                for (var r = -35f; r <= 35f; r += 17.5f)
                {
                    var (w, _) = PlaceKnuckles(h, knuckles, t, Quaternion.AngleAxis(r, t) * n);
                    m = Mathf.Min(m, (w - h.Upper.position).magnitude);
                }
                return m;
            }
            var shortBy = float.MaxValue;
            // Does an arm (shoulder, elbow, wrist; a 3 cm thick limb) go through the gun? Tested on the gun's collider,
            // parked where the hold points were measured: each world point is mapped into the gun's own space by the
            // placement's toGun, then out to the parked gun. The hand wraps the gun, so the last fifth of the forearm
            // is left out.
            var parked = gt.localToWorldMatrix;
            bool ArmThroughGun(System.Func<Vector3, Vector3> toGun, Vector3 sh, Vector3 elbow, Vector3 wrist)
            {
                if (LegacyTorso) return false;   // diagnosis: as the previews were made
                for (var k = 1; k <= 8; k++)
                {
                    var t = k / 9f;
                    // The half of the forearm next to the hand rests on the gun (a straight stock lies along it), so
                    // only up to the forearm's middle; the arm's core (half its radius) must stay out of the gun.
                    var p = t < 0.5f ? Vector3.Lerp(sh, elbow, t * 2f) : Vector3.Lerp(elbow, wrist, Mathf.Min(0.5f, (t - 0.5f) * 2f));
                    if (Physics.CheckSphere(parked.MultiplyPoint3x4(toGun(p)), _limbRadius * 0.5f)) return true;
                }
                return false;
            }
            // The firing elbow at least this far below the shoulder, the wrist bent at most this much (a relaxed
            // second pass only when no placement meets the first: bulky characters with wide shoulders).
            var minDrop = 0.04f;
            // A straight stock (no pistol grip) is held with the wrist bent further than a pistol grip.
            var maxBend = g.PistolGrip ? 45f : 55f;
            // One placement: its score (MaxValue if not holdable), how far short the support arm is, the support place.
            // Why placements were turned down (for the report when none is left).
            var why = new int[13];
            string[] whyNames = { "butt behind the back", "gun inside the body", "firing hand behind the hip", "firing hand out of reach", "firing elbow not down / wrist too bent", "firing arm through the gun", "support hand behind / out of reach", "support elbow not down / wrist too bent", "support arm through the gun", "firing arm into the body", "support arm into the body", "firing hand against the body", "support hand against the body" };
            (float score, float excess, int s) Eval(Vector3 d, float yaw, float pitch)
            {
                // Placed by the firing hand's grip, anywhere the right arm reaches in front of the body (owner,
                // 2026-10-06: the whole body adjusts; the stock no longer has to start in the armpit).
                var rot = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(0f, -90f, 0f);
                var origin = right.Upper.position + d - rot * Vector3.Scale(scale, new Vector3(g.GripC.x, g.GripC.y, g.Zc));
                Vector3 W(Vector3 local) => origin + rot * Vector3.Scale(scale, local);
                Vector3 ToGun(Vector3 world) => Vector3.Scale(Quaternion.Inverse(rot) * (world - origin), new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z));
                // The butt trapped in the armpit, never sticking out behind the back; nothing of the gun inside the torso.
                if (W(new Vector3(g.Butt.x, g.Butt.y, g.Zc)).z < armpit.z - 0.15f) { why[0]++; return (float.MaxValue, float.MaxValue, -1); }
                for (var k = 0; k <= 19; k++)
                {
                    // Along the gun itself: from the butt up to the bore at the muzzle (a stock below the barrel
                    // would otherwise put the front of the line in the air under the barrel).
                    var x = -0.47f + k * 0.05f;
                    var p = W(new Vector3(x, Mathf.Lerp(g.Butt.y, boreY, (x + 0.5f)), g.Zc));
                    // The gun may rest against the body (a stock under the arm) but not go into it: on the posed body's
                    // real surface, the gun's core (60 % of its half-thickness) must stay outside.
                    var inBody = _bodyCol != null ? Physics.CheckSphere(p, gunHalf * 0.6f, BodyMask, QueryTriggerInteraction.Ignore) || InTorso(p, 0f) : InTorso(p, gunHalf);
                    if (inBody) { why[1]++; return (float.MaxValue, float.MaxValue, -1); }
                }
                var gRight = -(rot * Vector3.forward);
                // Both hands in front of the body: the firing hand beside the hip or ahead of it, never behind it.
                if (W(gripEdge).z < right.Upper.position.z) { why[2]++; return (float.MaxValue, float.MaxValue, -1); }
                if (WristDist(right, W(gripEdge), rot * gripAxis, -gRight) > reachR) { why[3]++; return (float.MaxValue, float.MaxValue, -1); }
                if (BodyNear(W(gripMid), handRoom)) { why[11]++; return (float.MaxValue, float.MaxValue, -1); }
                // The firing elbow down, clamping the stock against the ribs (owner, 2026-10-06: the shotgun elbow was
                // raised too high): only hand places the arm reaches with its elbow well below the shoulder.
                var arm = SolveArm(right, W(gripEdge), rot * gripAxis, -gRight, FiringPole, 0.25f, 0.4f, 17.5f, 24);
                if (arm.drop < minDrop || arm.bend > maxBend || (arm.elbow.x - right.Upper.position.x) * Mathf.Sign(right.Upper.position.x) < ElbowOut) { why[4]++; return (float.MaxValue, float.MaxValue, -1); }
                if (arm.score >= 100f) { why[9]++; return (float.MaxValue, float.MaxValue, -1); }
                if (ArmThroughGun(ToGun, right.Upper.position, arm.elbow, arm.wrist)) { why[5]++; return (float.MaxValue, float.MaxValue, -1); }
                var excess = float.MaxValue;
                for (var si = 0; si < sup.Count; si++)
                {
                    if (W(sup[si].edge).z < left.Upper.position.z + 0.1f) continue;
                    if (BodyNear(W(supRoom[si].mid), supRoom[si].room)) { why[12]++; continue; }
                    var e = WristDist(left, W(sup[si].edge), rot * Vector3.right, rot * Vector3.up) - reachL;
                    excess = Mathf.Min(excess, e);
                    if (e > 0f) continue;
                    // The support elbow down under the gun too, not raised.
                    var supArm = SolveArm(left, W(sup[si].edge), rot * Vector3.right, rot * Vector3.up, SupportPole, 0.25f, 0.4f, 17.5f, 24);
                    // The support elbow below the shoulder and off the side of the body (chibi: arms read clearly).
                    var supOut = (supArm.elbow.x - left.Upper.position.x) * Mathf.Sign(left.Upper.position.x);
                    if (supArm.drop < 0.04f || supArm.bend > maxBend || supOut < -0.12f) { why[7]++; continue; }
                    if (supArm.score >= 100f) { why[10]++; continue; }
                    if (ArmThroughGun(ToGun, left.Upper.position, supArm.elbow, supArm.wrist)) { why[8]++; continue; }
                    // Prefer the first support part, then the least body turn, then the stock high under the arm.
                    var score = TargetCost(d, yaw, pitch) + HiddenUnderHead(W, g) * 15f + sup[si].part * 30f + (4 - si % 5) * 1f + Mathf.Abs(yaw) * 1.2f + arm.score * 2f + supArm.score * 2f + Mathf.Abs(pitch) * 0.3f + Mathf.Abs(d.y + 0.12f) * 10f + Mathf.Abs(d.x) * 6f;
                    return (score, e, si);
                }
                why[6]++;
                return (float.MaxValue, excess, -1);
            }
            var bestScore = float.MaxValue;
            (Vector3 d, float yaw, float pitch, int s) best = default;
            (Vector3 d, float yaw, float pitch) closest = default;
            void Try(Vector3 d, float yaw, float pitch)
            {
                var (score, excess, si) = Eval(d, yaw, pitch);
                if (excess < shortBy)
                {
                    shortBy = excess;
                    closest = (d, yaw, pitch);
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (d, yaw, pitch, si);
                }
            }
            // A locked hold: exactly its stored placement, no search.
            if (FixedPlacement.HasValue)
            {
                var fp = FixedPlacement.Value;
                var fsi = sup.Select((v, i) => (v, i)).Where(t => t.v.part == fp.part).OrderBy(t => Mathf.Abs(t.v.x - fp.x)).First().i;
                bestScore = 0f;
                best = (fp.d, fp.yaw, fp.pitch, fsi);
                goto Solved;
            }
            // The search window: round a reference target, or the general one.
            var tg = Target;
            float dx0 = tg?.D.x - tg?.Span.x ?? -0.28f, dx1 = tg?.D.x + tg?.Span.x ?? 0.2f;
            float dy0 = tg?.D.y - tg?.Span.y ?? -0.32f, dy1 = tg?.D.y + tg?.Span.y ?? 0.06f;
            float dz0 = tg?.D.z - tg?.Span.z ?? 0.1f, dz1 = tg?.D.z + tg?.Span.z ?? 0.5f;
            float yw0 = tg?.Yaw - tg?.YawSpan ?? -30f, yw1 = tg?.Yaw + tg?.YawSpan ?? 10f;
            float pt0 = tg?.Pitch - tg?.PitchSpan ?? -5f, pt1 = tg?.Pitch + tg?.PitchSpan ?? 5f;
            for (var dx = dx0; dx <= dx1 + 0.001f; dx += 0.04f * st)
            for (var dy = dy0; dy <= dy1 + 0.001f; dy += 0.038f * st)
            for (var dz = dz0; dz <= dz1 + 0.001f; dz += 0.04f * st)
            for (var yaw = yw0; yaw <= yw1 + 0.001f; yaw += 5f * st)
            for (var pitch = pt0; pitch <= pt1 + 0.001f; pitch += 5f * st)
                Try(new Vector3(dx, dy, dz), yaw, pitch);
            if (bestScore == float.MaxValue)
            {
                minDrop = 0.02f;
                maxBend = g.PistolGrip ? 55f : 65f;
                for (var dx = dx0; dx <= dx1 + 0.001f; dx += 0.04f * st)
                for (var dy = dy0; dy <= dy1 + 0.001f; dy += 0.038f * st)
                for (var dz = dz0; dz <= dz1 + 0.001f; dz += 0.04f * st)
                for (var yaw = yw0; yaw <= yw1 + 0.001f; yaw += 5f * st)
                for (var pitch = pt0; pitch <= pt1 + 0.001f; pitch += 5f * st)
                    Try(new Vector3(dx, dy, dz), yaw, pitch);
            }
            // Refine finely round the best placement (or round the nearest miss).
            var c = bestScore < float.MaxValue ? (best.d, best.yaw, best.pitch) : closest;
            if (!coarse)
            for (var dx = -0.02f; dx <= 0.021f; dx += 0.01f)
            for (var dy = -0.035f; dy <= 0.036f; dy += 0.0175f)
            for (var dz = -0.025f; dz <= 0.026f; dz += 0.0125f)
            for (var yaw = -2.5f; yaw <= 2.6f; yaw += 1.25f)
            for (var pitch = -1.5f; pitch <= 1.6f; pitch += 1.5f)
                Try(c.Item1 + new Vector3(dx, dy, dz), Mathf.Clamp(c.Item2 + yaw, yw0, yw1), Mathf.Clamp(c.Item3 + pitch, pt0, pt1));
            Solved:
            LastHoldScore = bestScore;
            if (bestScore == float.MaxValue)
            {
                note = $"{g.Name}: no reachable support place (support hand short by {shortBy * 100f:0} cm at best; turned down: {string.Join(", ", why.Select((n, i) => $"{whyNames[i]} {n}"))})";
                return false;
            }
            var chosen = sup[best.s];
            note = $"{g.Name}: firing grip {best.d.z * 100f:0} cm ahead of, {-best.d.y * 100f:0} cm below and {-best.d.x * 100f:0} cm inside the right shoulder, gun turned {-best.yaw:0}° inwards, tilted {best.pitch:0}°, support hand on the {g.Supports[chosen.part].name} at x {chosen.x:F2}";
            LastPlacement = (best.d, best.yaw, best.pitch, chosen.part, chosen.x);
            if (!apply) return true;
            gt.rotation = Quaternion.Euler(best.pitch, best.yaw, 0f) * Quaternion.Euler(0f, -90f, 0f);
            gt.position = right.Upper.position + best.d - gt.rotation * Vector3.Scale(scale, new Vector3(g.GripC.x, g.GripC.y, g.Zc));
            Physics.SyncTransforms();
            var gunRight = -gt.TransformDirection(Vector3.forward).normalized;
            var up = gt.TransformDirection(Vector3.up).normalized;
            var fwd = gt.TransformDirection(Vector3.right).normalized;
            var grip = Grip(gun, g.Id, g.GripC, g.GripAxis, g.GripReach, 1f, true, g.Zc);
            // Firing arm clamps the stock: elbow down and in against the side.
            ReachGrip(right, grip.edge, gt.TransformDirection(new Vector3(g.GripAxis.x, g.GripAxis.y, 0f)).normalized, -gunRight, FiringPole, 0.25f, 0.4f, false);
            for (var f = 2; f <= 4; f++) Wrap(right, f, grip.radius);
            Wrap(right, 1, grip.radius, 40f, 55f, 35f);   // index on the trigger
            if (g.PistolGrip) Wrap(right, 0, grip.radius, 35f, 40f, 30f);   // thumb round the far side
            else Wrap(right, 0, grip.radius, 30f, 30f, 20f);                // thumb over the wrist of the stock
            Gaps(right);
            var (edge, radius) = UnderEdge(gun, chosen.x, g.Supports[chosen.part].y, g.Zc);
            // Support arm: palm up under the gun, elbow down under it.
            ReachGrip(left, edge, fwd, up, SupportPole, 0.25f, 0.4f, false);
            for (var f = 1; f <= 4; f++) Grasp(left, f);
            Grasp(left, 0, 40f, 40f, 30f);
            Gaps(left);
            return true;
        }

        /// <summary>
        /// The lowest the elbow can be with the wrist at <paramref name="wrist"/>: the bottom of the circle of elbow
        /// positions (two-bone arm, shoulder fixed).
        /// </summary>
        private static float LowestElbow(HandRig h, Vector3 wrist)
        {
            // Sampled round the circle, skipping places inside the torso (where the elbow can't go).
            var sh = h.Upper.position;
            var side = Mathf.Sign(sh.x);
            var a = (h.Lower.position - sh).magnitude;
            var b = (h.Hand.position - h.Lower.position).magnitude;
            var d = Mathf.Clamp((wrist - sh).magnitude, 0.001f, (a + b) * 0.999f);
            var dir = (wrist - sh).normalized;
            var x = (a * a - b * b + d * d) / (2f * d);
            var r = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var centre = sh + dir * x;
            var u = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(dir, u).normalized;
            var low = float.MaxValue;
            for (var k = 0; k < 48; k++)
            {
                var th = k / 48f * Mathf.PI * 2f;
                var e = centre + (u * Mathf.Cos(th) + v * Mathf.Sin(th)) * r;
                if (InTorso(e, 0.03f)) continue;
                low = Mathf.Min(low, e.y);
            }
            return low;
        }

        /// <summary>
        /// Batch diagnosis (no pose change): character 03 with the shotgun. For points along the posed gun, whether each
        /// is inside the real body mesh (ray crossing count) and inside the measured torso, and which bone owns the
        /// nearest body vertex. Also the measured torso bands against the real body's extent at the gun's height.
        /// </summary>
        public static void DiagnoseHold()
        {
            var all = new StringBuilder();
            foreach (var model in new[] { "chr_girl", "chr_03", "chr_04" })
            foreach (var hold in new[] { Hold.Rifle, Hold.Shotgun, Hold.Sniper })
                all.AppendLine($"  {model} {hold}: " + DiagnoseOne(model, hold));
            Debug.Log("[ZOMBIE-DIAG-ALL]\n" + all);
        }

        private static string DiagnoseOne(string model, Hold hold)
        {
            var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
            // Each vertex's part, from the T-pose shape: body between the shoulder joints, hand beyond the wrists,
            // arm in between.
            var tpose = new Mesh();
            smr.BakeMesh(tpose, true);
            var shoulderX = smr.bones.Where(x => x != null && x.name.StartsWith("UpperArm.")).Select(x => Mathf.Abs(x.position.x)).Min();
            var wristX = smr.bones.Where(x => x != null && x.name.StartsWith("Hand.")).Select(x => Mathf.Abs(x.position.x)).Min();
            var part = tpose.vertices.Select(v =>
            {
                var x = Mathf.Abs(smr.transform.TransformPoint(v).x);
                return x <= shoulderX ? 0 : x < wristX ? 1 : 2;
            }).ToArray();
            Object.DestroyImmediate(tpose);
            var (gid, gsize, _) = Held(hold);
            var gun = Gun(gid, gsize);
            Pose(smr.bones, gun, hold);
            var note = LastNote;
            Diag.Clear();
            Object.DestroyImmediate(gun.GetComponent<MeshCollider>());
            // The posed mesh split into body, arms and hands, one collider each.
            var posed = new Mesh();
            smr.BakeMesh(posed, true);
            var tris = posed.triangles;
            var cols = new MeshCollider[3];
            var holders = new List<GameObject>();
            for (var p = 0; p < 3; p++)
            {
                var keep = new List<int>();
                for (var t = 0; t < tris.Length; t += 3)
                {
                    var votes = new[] { part[tris[t]], part[tris[t + 1]], part[tris[t + 2]] };
                    var main = votes.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
                    if (main == p) keep.AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                }
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(posed.vertices);
                m.SetTriangles(keep, 0);
                var h = new GameObject("part" + p);
                h.transform.SetParent(smr.transform, false);
                cols[p] = h.AddComponent<MeshCollider>();
                cols[p].sharedMesh = m;
                holders.Add(h);
            }
            Physics.SyncTransforms();
            Physics.queriesHitBackfaces = true;
            bool Inside(MeshCollider c, Vector3 q)
            {
                var n = 0;
                var from = q;
                for (var i = 0; i < 40; i++)
                {
                    if (!c.Raycast(new Ray(from, Vector3.right), out var hit, 5f)) break;
                    n++;
                    from = hit.point + Vector3.right * 0.0005f;
                }
                return n % 2 == 1;
            }
            // Points along the gun's whole length, through the middle of its height.
            var spec = hold == Hold.Shotgun ? ShotgunSpec : hold == Hold.Sniper ? SniperSpec : RifleSpec;
            var gt = gun.transform;
            int inBody = 0, inArm = 0, inHand = 0;
            var where = new StringBuilder();
            for (var k = 0; k <= 20; k++)
            {
                var x = -0.5f + k * 0.05f;
                var q = gt.TransformPoint(new Vector3(x, 0.17f, spec.Zc));
                if (Inside(cols[0], q)) { inBody++; where.Append($" body@{x:+0.00;-0.00}"); }
                else if (Inside(cols[1], q)) { inArm++; where.Append($" arm@{x:+0.00;-0.00}"); }
                else if (Inside(cols[2], q)) inHand++;
            }
            foreach (var h in holders) Object.DestroyImmediate(h);
            Object.DestroyImmediate(posed);
            Object.DestroyImmediate(gun);
            Object.DestroyImmediate(go);
            return $"inside the body {inBody}, inside an arm {inArm}, in a hand (gripping) {inHand}, of 21 points.{where} | {note}";
        }

/// <summary>
        /// Batch diagnosis (no pose change): how much of each arm ends up inside the body, for character 02 / 03 / 04
        /// holding the rifle and the shotgun, as the current previews were made and as the code now is. Then the same
        /// count under test motions an animation would add (spine twist and lean, a walking leg swing), the gun held in
        /// the right hand. Arm parts come from the T-pose shape: upper arm (shoulder joint to elbow), forearm (elbow to
        /// wrist), hand.
        /// </summary>
        public static void DiagnoseArms()
        {
            var all = new StringBuilder();
            foreach (var legacy in new[] { true, false })
            foreach (var model in new[] { "chr_girl", "chr_03", "chr_04" })
            foreach (var hold in new[] { Hold.Rifle, Hold.Shotgun })
            {
                LegacyTorso = legacy;
                all.AppendLine($"  [{(legacy ? "as in the previews" : "code now")}] {model} {hold}: " + ArmsInside(model, hold));
            }
            LegacyTorso = false;
            Debug.Log("[ZOMBIE-DIAG-ARMS]\n" + all);
        }

        private static string ArmsInside(string model, Hold hold)
        {
            var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
            Transform B(string n) => smr.bones.First(x => x != null && x.name == n);
            var shX = Mathf.Abs(B("UpperArm.R").position.x);
            var elX = Mathf.Abs(B("LowerArm.R").position.x);
            var wrX = Mathf.Abs(B("Hand.R").position.x);
            var tpose = new Mesh();
            smr.BakeMesh(tpose, true);
            // 0 body, 1 upper arm, 2 forearm, 3 hand; side +1 right (+x), -1 left.
            var part = new int[tpose.vertexCount];
            var side = new int[tpose.vertexCount];
            var tv = tpose.vertices;
            for (var i = 0; i < tv.Length; i++)
            {
                var p = smr.transform.TransformPoint(tv[i]);
                var x = Mathf.Abs(p.x);
                part[i] = x <= shX ? 0 : x < elX ? 1 : x < wrX ? 2 : 3;
                side[i] = p.x >= 0f ? 1 : -1;
            }
            Object.DestroyImmediate(tpose);
            var (gid, gsize, _) = Held(hold);
            var gun = Gun(gid, gsize);
            Pose(smr.bones, gun, hold);
            var note = LastNote;
            Diag.Clear();
            Object.DestroyImmediate(gun.GetComponent<MeshCollider>());
            gun.transform.SetParent(B("Hand.R"), true);
            Physics.queriesHitBackfaces = true;
            string Count()
            {
                var posed = new Mesh();
                smr.BakeMesh(posed, true);
                var tris = posed.triangles;
                var keep = new List<int>();
                for (var t = 0; t < tris.Length; t += 3)
                    if (part[tris[t]] == 0 && part[tris[t + 1]] == 0 && part[tris[t + 2]] == 0) keep.AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(posed.vertices);
                m.SetTriangles(keep, 0);
                var h = new GameObject("body");
                h.transform.SetParent(smr.transform, false);
                var col = h.AddComponent<MeshCollider>();
                col.sharedMesh = m;
                Physics.SyncTransforms();
                var inside = new int[2, 4];
                var total = new int[2, 4];
                var pv = posed.vertices;
                for (var i = 0; i < pv.Length; i++)
                {
                    if (part[i] == 0) continue;
                    var sd = side[i] > 0 ? 0 : 1;
                    total[sd, part[i]]++;
                    var q = smr.transform.TransformPoint(pv[i]);
                    // Inside if most of six rays cross the body an odd number of times (the body mesh is open where
                    // the arms were cut off, so one ray alone can miscount).
                    var votes = 0;
                    foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                    {
                        var n = 0;
                        var from = q;
                        for (var r = 0; r < 40; r++)
                        {
                            if (!col.Raycast(new Ray(from, dir), out var hit, 5f)) break;
                            n++;
                            from = hit.point + dir * 0.0005f;
                        }
                        if (n % 2 == 1) votes++;
                    }
                    if (votes >= 4) inside[sd, part[i]]++;
                }
                Object.DestroyImmediate(h);
                Object.DestroyImmediate(posed);
                string P(int sd, int pt) => total[sd, pt] == 0 ? "-" : $"{100f * inside[sd, pt] / total[sd, pt]:0}%";
                return $"right upper arm {P(0, 1)} / forearm {P(0, 2)} / hand {P(0, 3)}; left upper arm {P(1, 1)} / forearm {P(1, 2)} / hand {P(1, 3)}";
            }
            var report = new StringBuilder($"{note}\n      still: {Count()}");
            // Test motions (not gameplay animations): rotations added on top of the pose, then put back.
            var spine = B("Spine");
            var legL = B("UpperLeg.L");
            var legR = B("UpperLeg.R");
            var s0 = spine.localRotation;
            var l0 = legL.localRotation;
            var r0 = legR.localRotation;
            foreach (var (name, twist, lean, swing) in new[] { ("spine twist -20°", -20f, 0f, 0f), ("spine twist +20°", 20f, 0f, 0f), ("lean forward 10°", 0f, 10f, 0f), ("lean back 10°", 0f, -10f, 0f), ("walk, legs ±25°", 0f, 0f, 25f), ("walk + twist 15°", 15f, 5f, 25f) })
            {
                spine.localRotation = s0;
                spine.rotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(lean, Vector3.right) * spine.rotation;
                legL.localRotation = l0;
                legR.localRotation = r0;
                legL.rotation = Quaternion.AngleAxis(swing, Vector3.right) * legL.rotation;
                legR.rotation = Quaternion.AngleAxis(-swing, Vector3.right) * legR.rotation;
                report.Append($"\n      {name}: {Count()}");
            }
            spine.localRotation = s0;
            legL.localRotation = l0;
            legR.localRotation = r0;
            Object.DestroyImmediate(gun);
            Object.DestroyImmediate(go);
            return report.ToString();
        }

        /// <summary>Batch: for the shotgun and the sniper, which reference support place is reachable at each size.</summary>
        public static void LongGunTable()
        {
            var lines = new StringBuilder();
            foreach (var model in new[] { "chr_girl", "chr_03", "chr_04" })
            foreach (var g in new[] { RifleSpec, ShotgunSpec, SniperSpec })
            {
                foreach (var size in new[] { 0.8f, 0.7f, 0.6f, 0.5f, 0.4f })
                {
                    var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
                    var gun = Gun(g.Id, size);
                    UnderarmLong(Rig(smr.bones, "R"), Rig(smr.bones, "L"), gun, g, false, out var note);
                    lines.AppendLine($"  {model} {size:F1} m {(note.Contains("no reachable") ? g.Name + ": none" : note)}");
                    Object.DestroyImmediate(gun);
                    Object.DestroyImmediate(go);
                }
            }
            Debug.Log("[ZOMBIE-LONGGUN-TABLE]\n" + lines);
        }

        /// <summary>How much longer the arms must be for the shouldered hold (support hand at 62 % of the rifle).</summary>
        public static float NeededArmScale = 1f;

        /// <summary>
        /// The handguard's bottom-right edge at <paramref name="t"/> along the rifle (where the support hand's knuckles
        /// go, palm up under it, fingers wrapping up its right side) and the radius the fingers wrap at.
        /// </summary>
        private static (Vector3 edge, float radius) HandguardEdge(GameObject gun, float t)
        {
            // Measured with rays (the low-poly handguard has no vertices along its middle): from below, and from the right.
            var mesh = PackArt.Model("ar_gun");
            var x = mesh.bounds.min.x + t * mesh.bounds.size.x;
            var g = gun.transform;
            var bore = PackArt.MuzzleOf("ar_gun").y;
            var centre = g.TransformPoint(new Vector3(x, bore, 0f));
            var up = g.TransformDirection(Vector3.up).normalized;
            var right = -g.TransformDirection(Vector3.forward).normalized;
            var bottom = Surface(centre, up);
            var side = Surface(centre, -right);
            var halfH = Vector3.Dot(centre - bottom, up);
            var halfW = Vector3.Dot(side - centre, right);
            var edge = centre - up * (halfH + FingerRadius * 0.5f) + right * (halfW + FingerRadius);
            return (edge, (halfH + halfW) * 0.5f + FingerRadius);
        }

        /// <summary>Batch: the arm length the shouldered hold needs for several rifle sizes (for the owner's choice).</summary>
        public static void ArmTable()
        {
            var lines = new StringBuilder();
            foreach (var size in new[] { 0.8f, 0.75f, 0.7f, 0.65f, 0.6f, 0.55f }) lines.AppendLine($"  rifle {size:F2} m: arms {ComputeArmScale(size):F2}x, butt to trigger {0.336f * size:F2} m");
            Debug.Log("[ZOMBIE-ARMS-TABLE]\n" + lines);
        }

        /// <summary>How much longer character 02's arms need to be for the shouldered rifle hold.</summary>
        public static float ComputeArmScale(float rifleSize = 1.4f)
        {
            var (go, smr) = Spawn(null, new StringBuilder(), false, false, 1f);
            var gun = Gun("ar_gun", rifleSize);
            Shouldered(Rig(smr.bones, "R"), Rig(smr.bones, "L"), gun, false);
            var k = NeededArmScale;
            Object.DestroyImmediate(gun);
            Object.DestroyImmediate(go);
            Debug.Log($"[ZOMBIE-ARMS] the shouldered hold needs arms {k:F2}× as long");
            return k;
        }

        public static readonly StringBuilder Diag = new StringBuilder();
        public static Vector3 GripR, GripL;

        /// <summary>Whether each fingertip rests on the weapon, for the report.</summary>
        private static void Gaps(HandRig h)
        {
            if (_gun == null) return;
            var names = new[] { "thumb", "index", "middle", "ring", "little" };
            var line = new StringBuilder($"  {h.Hand.name} fingertips to the weapon:");
            for (var f = 0; f < 5; f++)
            {
                var b = h.Fingers[f];
                var tip = b[2].position + (b[2].position - b[1].position) * 0.9f;
                var fwd = (h.Hand.rotation * Quaternion.Inverse(h.HandRest)) * h.F0;
                var bend = Vector3.Angle(fwd, (tip - b[2].position).normalized);
                line.Append($" {names[f]} {(Physics.CheckSphere(tip, FingerRadius * 1.6f) ? "touching" : "free")} (tip bent {bend:0}°)");
            }
            Diag.AppendLine(line.ToString());
            if (h.Hand.name.EndsWith("R")) GripR = h.Fingers[2][0].position; else GripL = h.Fingers[2][0].position;
        }

        /// <summary>Gives the weapon a mesh collider for the surface and finger contact tests (editor posing only).</summary>
        private static void AttachCollider(GameObject gun)
        {
            var mc = gun.GetComponent<MeshCollider>();
            if (mc == null) mc = gun.AddComponent<MeshCollider>();
            mc.sharedMesh = gun.GetComponent<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            _gun = mc;
        }

        /// <summary>What the last rifle pose chose (for the report).</summary>
        public static string LastNote = "";

        /// <summary>How far the arm reaches from the shoulder joint to the wrist.</summary>
        private static float Reach(HandRig h) => (h.Lower.position - h.Upper.position).magnitude + (h.Hand.position - h.Lower.position).magnitude;

        /// <summary>
        /// Poses the character (its bones, already scaled to game size) holding <paramref name="gun"/> (scaled like the
        /// game's held weapons) in the given hold. Places the gun too.
        /// </summary>
        public static void Pose(Transform[] bones, GameObject gun, Hold hold)
        {
            _gun = null;
            var right = Rig(bones, "R");
            var left = Rig(bones, "L");
            // Long guns: the full-body hold (owner, 2026-10-06), verified clean; no fallback that clips.
            if (hold == Hold.Shotgun || hold == Hold.Sniper || hold == Hold.Rifle)
            {
                if (!FullBodyHold(bones, right, left, gun, hold == Hold.Shotgun ? ShotgunSpec : hold == Hold.Sniper ? SniperSpec : RifleSpec, out LastNote))
                    Debug.LogWarning("[ZOMBIE-HOLD] " + LastNote);
                return;
            }
            // The pistol: the isosceles arms on the best clean body pose (HoldBody.FullBodyPistol).
            FullBodyPistol(bones, right, left, gun);
        }

        /// <summary>The isosceles pistol hold's arms and hands, for the body as it is posed now.</summary>
        /// <summary>
        /// The one-handed pistol (owner, 2026-10-07: held in one hand, the hand extended farther from the body): the
        /// firing arm reaching out towards the target to 97 % of its length (straight but not locked), the pistol level
        /// in front of the firing shoulder; the free arm relaxed at the side, held <paramref name="outward"/> away from
        /// the body so it can't sink into it, the elbow a little bent.
        /// </summary>
        private static void OneHandPistolArms(HandRig right, HandRig left, GameObject gun, float outward, float lateral = 0f)
        {
            var gt = gun.transform;
            Vector3 P(float x, float y, float z = 0f) => gt.TransformPoint(new Vector3(x, y, z));
            Vector3 D(float x, float y, float z = 0f) => gt.TransformDirection(new Vector3(x, y, z)).normalized;
            var rs = right.Upper.position;
            var ext = Reach(right) * 0.97f;
            const float drop = 0.06f;
            var forward = Mathf.Sqrt(Mathf.Max(0.01f, ext * ext - drop * drop));
            // Straight ahead of the firing shoulder, or angled out to its side by lateral (m) on a bulky chest.
            var side = Mathf.Sign(rs.x - left.Upper.position.x);
            forward = Mathf.Sqrt(Mathf.Max(0.01f, forward * forward - lateral * lateral));
            var gripTarget = new Vector3(rs.x + side * lateral, rs.y - drop, rs.z + forward);
            gt.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            var gunRight = -D(0f, 0f, 1f);
            var gripLocal = new Vector3(-0.319f, 0.173f, 0f);
            gt.position = gripTarget - gt.rotation * Vector3.Scale(gt.localScale, gripLocal);
            AttachCollider(gun);
            var gripUp = D(0.235f, 0.972f);
            var pg = Grip(gun, "pistol", new Vector2(-0.319f, 0.173f), new Vector2(0.235f, 0.972f), 0.12f, 1f);
            ReachGrip(right, pg.edge, gripUp, -gunRight, new Vector3(0.6f, -0.8f, 0f), 0.15f, 0.2f);
            var pr = pg.radius;
            Wrap(right, 2, pr);
            Wrap(right, 3, pr);
            Wrap(right, 4, pr);
            Wrap(right, 1, pr, 40f, 55f, 35f);   // index on the trigger
            Wrap(right, 0, pr, 35f, 40f, 30f);   // thumb round the far side
            Gaps(right);
            // The free arm hangs at the side, out from the body.
            var hang = new Vector3(-side * outward, -1f, 0.06f).normalized;
            left.Upper.rotation = Quaternion.FromToRotation(left.Lower.position - left.Upper.position, hang) * left.Upper.rotation;
            var fore = (hang + new Vector3(0f, 0f, 0.3f)).normalized;
            left.Lower.rotation = Quaternion.FromToRotation(left.Hand.position - left.Lower.position, fore) * left.Lower.rotation;
        }

        private static void PistolArms(HandRig right, HandRig left, GameObject gun, float extension = 0.92f)
        {
            var gt = gun.transform;
            Vector3 P(float x, float y, float z = 0f) => gt.TransformPoint(new Vector3(x, y, z));
            Vector3 D(float x, float y, float z = 0f) => gt.TransformDirection(new Vector3(x, y, z)).normalized;
            // Isosceles (owner, 2026-10-06: a practical firing stance; 2026-10-07: both hands, both arms as far forward
            // as they go): both arms extended forward to <paramref name="extension"/> of their reach (nearly straight,
            // not locked), the pistol on the centre line just below shoulder height.
            var rs = right.Upper.position;
            var ext = Reach(right) * extension;
            var forward = Mathf.Sqrt(Mathf.Max(0.01f, ext * ext - (rs.x - 0.07f) * (rs.x - 0.07f) - 0.1f * 0.1f));
            // Slightly right of centre, in line with a right-hander's dominant eye: the firing wrist stays straighter.
            var gripTarget = new Vector3(0.07f, rs.y - 0.1f, rs.z + forward);
            gt.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            var gunRight = -D(0f, 0f, 1f);
            var gripLocal = new Vector3(-0.319f, 0.173f, 0f);
            gt.position = gripTarget - gt.rotation * Vector3.Scale(gt.localScale, gripLocal);
            var gripCentre = P(-0.319f, 0.173f);
            var gripUp = D(0.235f, 0.972f);
            var gripR = 0.07f * gt.localScale.z;
            AttachCollider(gun);
            var gripFwd = D(0.972f, -0.235f);
            var pg = Grip(gun, "pistol", new Vector2(-0.319f, 0.173f), new Vector2(0.235f, 0.972f), 0.12f, 1f);
            ReachGrip(right, pg.edge, gripUp, -gunRight, new Vector3(0.5f, -0.85f, 0.2f), 0.15f, 0.2f);
            var pr = pg.radius;
            Wrap(right, 2, pr);
            Wrap(right, 3, pr);
            Wrap(right, 4, pr);
            Wrap(right, 1, pr, 40f, 55f, 35f);   // index on the trigger
            Wrap(right, 0, pr, 10f, 10f, 5f);    // thumb forward along the frame
            Gaps(right);
            // Support hand: on the left side of the grip, over the right hand's fingers, thumb forward.
            // Over the right hand's fingers: the left side of the grip plus a finger's thickness.
            // Knuckles just in front of the right hand's fingers on the left side; fingers wrap over them.
            var support = Grip(gun, "pistol", new Vector2(-0.33f, 0.13f), new Vector2(0.235f, 0.972f), 0.12f, -1f).edge - gunRight * 0.02f + gripFwd * 0.035f;
            ReachGrip(left, support, gripUp, gunRight, new Vector3(-0.5f, -0.85f, 0.2f), 0.15f, 0.2f);
            // Round the grip and the right hand's fingers on it.
            for (var fi = 1; fi <= 4; fi++) Wrap(left, fi, pr + 0.035f);
            Wrap(left, 0, pr, 10f, 10f, 5f);     // thumb forward along the frame
            Gaps(left);
        }

        // ------------------------------------------------------------------ previews

        /// <summary>
        /// Batch: character 02 coloured (Marine camouflage, ranger-green gloves, black boots) holding the rifle and
        /// the pistol, with close-ups of the hands, to logs/char-preview/chr_girl-hold-*.png. Editor only.
        /// </summary>
        public static void PreviewHolds()
        {
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var camo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + "camo_marines.png");
            var log = new StringBuilder();
            foreach (var hold in new[] { Hold.Rifle, Hold.Pistol, Hold.Shotgun, Hold.Sniper })
            {
                var (go, smr) = Spawn(camo, log, hold == Hold.Rifle, true);
                var (id, size, name) = Held(hold);
                var gun = Gun(id, size);
                LastNote = "";
                Pose(smr.bones, gun, hold);
                Blade(go, gun);
                log.AppendLine(LastNote);
                log.AppendLine($"{hold}: body turned {BodyTurn:0}° from the aim");
                log.Append(Diag);
                Diag.Clear();
                var hand = smr.bones.First(b => b.name == "Hand.R").position;
                var support = smr.bones.First(b => b.name == "Hand.L").position;
                Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-35.png"), new Vector3(0f, 1.1f, 0.3f), 1.35f, 35f, 10f);
                Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-side.png"), new Vector3(0f, 1.1f, 0.3f), 1.35f, 90f, 5f);
                // Close-ups centred on each hand's knuckles, from its own side, from below, and without the gun.
                foreach (var withGun in new[] { true, false })
                {
                    gun.SetActive(withGun);
                    var tag = withGun ? "" : "-nogun";
                    Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-right-side{tag}.png"), GripR, 0.16f, 90f, 0f);
                    Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-right-front{tag}.png"), GripR, 0.16f, 0f, 0f);
                    Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-left-side{tag}.png"), GripL, 0.16f, -90f, 0f);
                    Render(Path.Combine(OutDir, $"chr_girl-hold-{name}-below{tag}.png"), (GripR + GripL) * 0.5f, 0.3f, 0f, -75f);
                }
                gun.SetActive(true);
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-HOLD]\n" + log);
        }

        /// <summary>Batch check: the T-pose with every finger curled into a fist, close-ups of both hands (front, below, side).</summary>
        public static void CurlTest()
        {
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var (go, smr) = Spawn(null, new StringBuilder(), false);
            var right = Rig(smr.bones, "R");
            var left = Rig(smr.bones, "L");
            foreach (var h in new[] { right, left })
            {
                for (var f = 1; f <= 4; f++) Curl(h, f, 75f, 85f, 60f);
                Curl(h, 0, 30f, 35f, 20f);
            }
            var hr = right.Hand.position;
            Render(Path.Combine(OutDir, "curl-right-front.png"), hr + new Vector3(0.1f, 0f, 0f), 0.22f, 0f, 5f);
            Render(Path.Combine(OutDir, "curl-right-below.png"), hr + new Vector3(0.1f, 0f, 0f), 0.22f, 0f, -80f);
            Render(Path.Combine(OutDir, "curl-right-out.png"), hr + new Vector3(0.1f, 0f, 0f), 0.22f, -90f, 5f);
            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Batch: characters 03 (Space Force camouflage) and 04 (Navy) as delivered in a T-pose, their faces, and holding
        /// each weapon, to logs/char-preview/chr_0N-final-*.png.
        /// </summary>
        /// <summary>Batch: the secondary characters (scientist, zombies) coloured, T-pose and face, to logs/char-preview.</summary>
        public static void PreviewSecondary()
        {
            foreach (var (model, fast) in new[] { ("npc_scientist", false), ("zmb_1", true), ("zmb_1", false), ("zmb_2", true), ("zmb_2", false), ("boss_1", false), ("boss_bear", false), ("zmb_dog", false), ("zmb_rat", false), ("boss_tiger", false) })
            {
                FastVariant = fast;
                PreviewCharacter(model, "camo_army", false);
                FastVariant = false;
                // The fast variant's renders under their own name.
                if (fast)
                    foreach (var file in Directory.GetFiles(OutDir, $"{model}-final-*.png"))
                    {
                        var to = Path.Combine(OutDir, Path.GetFileName(file).Replace($"{model}-final-", $"{model}-fast-final-"));
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(file, to);
                    }
            }
        }

        /// <summary>Batch: every delivered character's colours, one in-game camera image each (owner, 2026-10-07).</summary>
        public static void PreviewAllColours()
        {
            foreach (var (model, camoName) in new[] { ("chr_01", "camo_airforce"), ("chr_girl", "camo_marines"), ("chr_03", "camo_army"), ("chr_04", "camo_navy") })
                PreviewCharacter(model, camoName, false);
            PreviewSecondary();
        }

        public static void PreviewOthers()
        {
            foreach (var (model, camoName) in new[] { ("chr_03", "camo_army"), ("chr_04", "camo_navy") }) PreviewCharacter(model, camoName, true);
        }

        private static void PreviewCharacter(string model, string camoName, bool holds)
        {
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var camo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + camoName + ".png");
            var log = new StringBuilder();
            // One image for the review, in the in-game camera only (owner, 2026-10-07: no other angles).
            {
                var (go, _) = Spawn(camo, log, true, false, -1f, model);
                go.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                // The game camera's angle, framed on the model (an animal is lower and longer than a person).
                if (IsAnimal(model))
                {
                    var b = go.GetComponentInChildren<SkinnedMeshRenderer>().bounds;
                    RenderGame(Path.Combine(OutDir, $"{model}-final-colour.png"), Mathf.Max(0.5f, Mathf.Max(b.extents.x, b.extents.z) * 1.4f), b.center.y);
                }
                else RenderGame(Path.Combine(OutDir, $"{model}-final-colour.png"));
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-FINAL-03]\n" + log + Diag);
            Diag.Clear();
        }

        /// <summary>Batch: the final appearance preview of character 02 (T-pose as delivered and holding the rifle).</summary>
        public static void PreviewFinal()
        {
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var camo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + "camo_marines.png");
            var log = new StringBuilder();
            foreach (var yaw in new[] { 0f, 35f, 90f, 180f })
            {
                var (go, _) = Spawn(camo, log, yaw == 0f);
                Render(Path.Combine(OutDir, $"chr_girl-final-tpose-{yaw:0}.png"), new Vector3(0f, 1.1f, 0f), 1.25f, yaw, 5f);
                Object.DestroyImmediate(go);
            }
            {
                var (go, _) = Spawn(camo, log, false);
                Render(Path.Combine(OutDir, "chr_girl-final-face.png"), new Vector3(0f, 1.6f, 0.1f), 0.42f, 0f, 3f);
                Object.DestroyImmediate(go);
            }
            foreach (var hold in new[] { Hold.Rifle, Hold.Pistol, Hold.Shotgun, Hold.Sniper })
            foreach (var (yaw, view) in new[] { (35f, "35"), (90f, "side"), (180f, "game") })
            {
                var (go, smr) = Spawn(camo, log, false);
                var (id, size, name) = Held(hold);
                var file = $"chr_girl-final-{name}-{view}.png";
                var gun = Gun(id, size);
                Pose(smr.bones, gun, hold);
                Blade(go, gun);
                if (yaw == 180f) RenderGame(Path.Combine(OutDir, file));
                else Render(Path.Combine(OutDir, file), new Vector3(0f, 1.1f, 0.3f), 1.35f, yaw, 10f);
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-FINAL]\n" + log);
        }

        /// <summary>The arm length factor for character 02 (owner, 2026-10-06: option B), computed once per run.</summary>
        private static float _armScale = -1f;

        /// <summary>
        /// Owner, 2026-10-07: character 03's arms are lengthened (its chest and belly stand out about as far as its arms
        /// reach, so its hands couldn't get in front of its body): 36 % longer upper arm and forearm. Others as delivered.
        /// </summary>
        public static float ArmScaleFor(string model) => model == "chr_03" ? 1.36f : ArmScale;

        public static float ArmScale
        {
            get
            {
                // The shortest arms that reach the shouldered hold, plus 3 % so the elbows keep a slight bend.
                // Owner, 2026-10-06: the original arm length; never lengthened to suit a hold.
                if (_armScale < 0f) _armScale = 1f;
                return _armScale;
            }
        }

        private static (GameObject, SkinnedMeshRenderer) Spawn(Texture2D camo, StringBuilder log, bool report, bool debugGloves = false, float armScale = -1f, string model = "chr_girl")
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterInspector.Folder + $"/{model}.fbx"));
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = r == smr;
            // To the game height, from the delivered mesh's own height (feet to top).
            var baked = new Mesh();
            // Measured as CharacterInspector does (the validation report's vertex extent).
            smr.BakeMesh(baked, true);
            var top = baked.vertices.Max(v => smr.transform.TransformPoint(v).y);
            Object.DestroyImmediate(baked);
            // The animals to their type's height (GameTuning: Dog 1.1 m, Rat 0.45 m, Tiger 1.6 m).
            var height = model == "zmb_dog" ? 1.1f : model == "zmb_rat" ? 0.45f : model == "boss_tiger" ? 1.6f : CharacterRig.Height;
            go.transform.localScale = Vector3.one * (height / top);
            if (report) log.AppendLine($"  {model}: delivered height {top:F3} m, scaled to {height} m");
            // The animals: no arms to fit; their one region is painted by AnimalColour (never camouflage).
            if (IsAnimal(model))
            {
                var painted = Colour(smr, camo, debugGloves, Color.grey, model);
                if (report) log.Append(painted);
                return (go, smr);
            }
            LengthenArms(smr, armScale < 0f ? ArmScaleFor(model) : armScale);
            var line = Colour(smr, camo, debugGloves, UnmarkedColour(model), model);
            MeasureTorso(smr);
            if (report) log.AppendLine($"  arms lengthened {(armScale < 0f ? ArmScale : armScale):F2}× (upper arm and forearm)");
            if (report) log.Append(line);
            return (go, smr);
        }

        /// <summary>
        /// Turns the posed character and its gun together about the vertical so the gun points straight ahead: the
        /// bladed body of the underarm / hip stances (the shooter turns, the gun points at the target). The game must
        /// turn the body by the same angle when it faces the aim.
        /// </summary>
        private static void Blade(GameObject character, GameObject gun)
        {
            var muzzle = gun.transform.TransformDirection(Vector3.right);
            muzzle.y = 0f;
            var turn = Vector3.SignedAngle(muzzle, Vector3.forward, Vector3.up);
            var pivot = character.transform.position;
            character.transform.RotateAround(pivot, Vector3.up, turn);
            gun.transform.RotateAround(pivot, Vector3.up, turn);
            Physics.SyncTransforms();
            BodyTurn = -turn;
        }

        /// <summary>How far the last hold turns the body from the gun's direction (degrees, + to the right).</summary>
        public static float BodyTurn;

        /// <summary>The game's held model and size for a hold (Survivor.HeldModel), and the preview's file name.</summary>
        private static (string id, float size, string name) Held(Hold hold)
        {
            switch (hold)
            {
                case Hold.Pistol: return ("pistol", Survivor.PistolSize, "pistol");
                case Hold.Shotgun: return ("shotgun", Survivor.ShotgunSize, "shotgun");
                case Hold.Sniper: return ("sniper_gun", Survivor.SniperSize, "sniper");
                default: return ("ar_gun", Survivor.RifleSize, "rifle");
            }
        }

        private static GameObject Gun(string id, float size)
        {
            var gun = new GameObject(id);
            gun.AddComponent<MeshFilter>().sharedMesh = PackArt.Model(id);
            gun.AddComponent<MeshRenderer>().sharedMaterial = ArtMaterials.Stylized;
            gun.transform.localScale = new Vector3(size, size * Survivor.Chunky, size * Survivor.Chunky);
            return gun;
        }

        /// <summary>An orthographic shot of <paramref name="centre"/>; the camera at yaw (0 = in front, facing the character) and pitch.</summary>
        private static void Render(string file, Vector3 centre, float halfSize, float yaw, float pitch)
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = halfSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.32f, 0.36f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.rotation = Quaternion.Euler(pitch, 180f + yaw, 0f);
            cam.transform.position = centre - cam.transform.forward * 20f;
            var light = new GameObject("light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(35f, 160f + yaw, 0f);
            Shoot(cam, file, 512);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
        }

        private static void RenderGame(string file, float size = 1.4f, float centreY = 1.1f)
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.36f, 0.25f);
            cam.transform.rotation = Quaternion.Euler(CameraRig.Tilt, 180f, 0f);
            cam.transform.position = new Vector3(0f, centreY, 0.3f) - cam.transform.forward * 20f;
            var light = new GameObject("light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(55f, 150f, 0f);
            Shoot(cam, file, 512);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
        }

        // ------------------------------------------------------------------ weapon probe

        /// <summary>Batch: side views of the held weapons with a grid (tenths of the length from the back end, bore line).</summary>
        public static void ProbeWeapons()
        {
            foreach (var id in new[] { "shotgun", "sniper_gun" })
            {
                var mesh = PackArt.Model(id);
                var go = new GameObject(id);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = ArtMaterials.Stylized;
                var b = mesh.bounds;
                var muzzle = PackArt.MuzzleOf(id);
                var lines = new List<GameObject>();
                for (var i = 0; i <= 10; i++)
                {
                    var x = b.min.x + b.size.x * i / 10f;
                    lines.Add(Bar(new Vector3(x, b.center.y, 0.3f), new Vector3(0.003f, b.size.y * 1.2f, 0.003f), i % 5 == 0 ? Color.red : Color.yellow));
                }
                lines.Add(Bar(new Vector3(b.center.x, muzzle.y, 0.3f), new Vector3(b.size.x * 1.1f, 0.003f, 0.003f), Color.cyan));
                for (var j = 1; j <= 4; j++)
                    lines.Add(Bar(new Vector3(b.center.x, muzzle.y - 0.05f * j, 0.3f), new Vector3(b.size.x * 1.1f, 0.0015f, 0.002f), Color.white));
                var cam = new GameObject("cam").AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = b.size.x * 0.6f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.3f, 0.32f, 0.36f);
                cam.transform.position = b.center + new Vector3(0f, 0f, 10f);
                cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var light = new GameObject("light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
                Shoot(cam, Path.Combine(OutDir, $"probe-{id}.png"), 640);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(go);
                foreach (var l in lines) Object.DestroyImmediate(l);
                Debug.Log($"[ZOMBIE-PROBE] {id}: bounds {b.min:F3}..{b.max:F3}, muzzle {muzzle:F3} (grid: tenths of length from the back end; cyan bore, white lines every 0.05 below it)");
            }
        }

        /// <summary>Batch: each weapon's solid intervals (model y) along its length at the centre plane, with their widths.</summary>
        public static void ProfileWeapons()
        {
            var log = new StringBuilder();
            Physics.queriesHitBackfaces = true;
            foreach (var id in new[] { "shotgun", "sniper_gun", "ar_gun" })
            {
                var go = new GameObject(id);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = PackArt.Model(id);
                Physics.SyncTransforms();
                log.AppendLine(id);
                for (var x = -0.49f; x <= 0.5f; x += 0.025f)
                {
                    // Vertices in the slice, grouped into spans along y (a gap over 1.5 cm splits them), with their z range.
                    var vs = mc.sharedMesh.vertices.Where(v => Mathf.Abs(v.x - x) < 0.0125f).OrderBy(v => v.y).ToList();
                    var line = new StringBuilder($"  x {x:+0.000;-0.000}:");
                    for (var i = 0; i < vs.Count;)
                    {
                        var j = i;
                        while (j + 1 < vs.Count && vs[j + 1].y - vs[j].y < 0.015f) j++;
                        var span = vs.GetRange(i, j - i + 1);
                        line.Append($" [y {span[0].y:F3}..{span[span.Count - 1].y:F3} z {span.Min(v => v.z):F3}..{span.Max(v => v.z):F3}]");
                        i = j + 1;
                    }
                    log.AppendLine(line.ToString());
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-PROFILE]\n" + log);
        }

        private static GameObject Bar(Vector3 at, Vector3 size, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.position = at;
            g.transform.localScale = size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        internal static void Shoot(Camera cam, string file, int size)
        {
            var rt = new RenderTexture(size, size, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            t.Apply();
            File.WriteAllBytes(file, t.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(rt);
            cam.targetTexture = null;
        }
    }
}
