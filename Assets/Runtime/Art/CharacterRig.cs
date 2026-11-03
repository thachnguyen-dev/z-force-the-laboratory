using System.Collections.Generic;
using UnityEngine;
using ModelData = ZombieGame.Art.CharacterModelLibrary.ModelData;

namespace ZombieGame.Art
{
    /// <summary>
    /// The owner's character models in the game (owner, 2026-10-07: connected), on their own delivered skeletons
    /// (docs/3-assets-art-direction.md 4.1 people, 4.4 four-legged), animated in code (CharacterPuppet, ZombiePuppet).
    /// The editor bake (EditorTools.CharacterBake) colours each model, repairs its skinning, merges the fingers into the
    /// hands and writes its mesh and skeleton (CharacterModelLibrary). <see cref="Build"/> makes the bones (rest: identity
    /// rotations, at the delivered joints) and a SkinnedMeshRenderer.
    /// </summary>
    public static class CharacterRig
    {
        /// <summary>People's bones (the delivered rig without the fingers, which ride the hands).</summary>
        public enum Bone
        {
            Hips, Spine, Chest, Neck, Head,
            UpperArmL, LowerArmL, HandL, UpperArmR, LowerArmR, HandR,
            UpperLegL, LowerLegL, FootL, UpperLegR, LowerLegR, FootR,
            ShoulderL, ShoulderR,
        }

        public const int BoneCount = 19;

        /// <summary>Each person bone's parent (-1 = the root).</summary>
        public static readonly int[] Parent =
        {
            -1, (int)Bone.Hips, (int)Bone.Spine, (int)Bone.Chest, (int)Bone.Neck,
            (int)Bone.ShoulderL, (int)Bone.UpperArmL, (int)Bone.LowerArmL, (int)Bone.ShoulderR, (int)Bone.UpperArmR, (int)Bone.LowerArmR,
            (int)Bone.Hips, (int)Bone.UpperLegL, (int)Bone.LowerLegL, (int)Bone.Hips, (int)Bone.UpperLegR, (int)Bone.LowerLegR,
            (int)Bone.Chest, (int)Bone.Chest,
        };

        /// <summary>Four-legged bones (docs 4.4: the Dog, the Rat, the Tiger).</summary>
        public enum Quad
        {
            Hips, Spine, Chest, Neck, Head, Jaw,
            FrontShoulderL, FrontUpperLegL, FrontLowerLegL, FrontFootL,
            FrontShoulderR, FrontUpperLegR, FrontLowerLegR, FrontFootR,
            HindUpperLegL, HindLowerLegL, HindAnkleL, HindFootL,
            HindUpperLegR, HindLowerLegR, HindAnkleR, HindFootR,
            Tail1, Tail2, Tail3, Tail4,
        }

        public const int QuadCount = 26;

        public static readonly int[] QuadParent =
        {
            -1, (int)Quad.Hips, (int)Quad.Spine, (int)Quad.Chest, (int)Quad.Neck, (int)Quad.Head,
            (int)Quad.Chest, (int)Quad.FrontShoulderL, (int)Quad.FrontUpperLegL, (int)Quad.FrontLowerLegL,
            (int)Quad.Chest, (int)Quad.FrontShoulderR, (int)Quad.FrontUpperLegR, (int)Quad.FrontLowerLegR,
            (int)Quad.Hips, (int)Quad.HindUpperLegL, (int)Quad.HindLowerLegL, (int)Quad.HindAnkleL,
            (int)Quad.Hips, (int)Quad.HindUpperLegR, (int)Quad.HindLowerLegR, (int)Quad.HindAnkleR,
            (int)Quad.Hips, (int)Quad.Tail1, (int)Quad.Tail2, (int)Quad.Tail3,
        };

        /// <summary>The delivered bone name for a rig bone (FrontShoulderL → FrontShoulder.L).</summary>
        public static string Delivered(string rigName) =>
            rigName.EndsWith("L") && rigName.Length > 1 && char.IsLower(rigName[rigName.Length - 2]) ? rigName.Substring(0, rigName.Length - 1) + ".L"
            : rigName.EndsWith("R") && rigName.Length > 1 && char.IsLower(rigName[rigName.Length - 2]) ? rigName.Substring(0, rigName.Length - 1) + ".R"
            : rigName;

        /// <summary>A playable character's height in the game (m): chibi proportions (owner: Brawl Stars). PLACEHOLDER.</summary>
        public const float Height = 2.2f;

        /// <summary>The model a playable character wears (null: still the capsule).</summary>
        public static ModelData ForCharacter(string characterId) => CharacterModelLibrary.ForCharacter(characterId);

        public const string ResourceFolder = "Characters/";

        public static Mesh LoadMesh(string id) => Resources.Load<Mesh>(ResourceFolder + id);

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>The model's material: the stylised shader, its branch camouflage shaded by the mesh's tones.</summary>
        public static Material MaterialFor(ModelData m)
        {
            if (string.IsNullOrEmpty(m.camo)) return ArtMaterials.Stylized;
            if (Materials.TryGetValue(m.camo, out var mat) && mat != null) return mat;
            mat = new Material(ArtMaterials.Stylized) { name = m.camo };
            var tex = Resources.Load<Texture2D>("Art/Ground/" + m.camo);
            if (tex != null) mat.SetTexture("_CamoTex", tex);
            mat.SetFloat("_CamoShade", 1f);
            Materials[m.camo] = mat;
            return mat;
        }

        /// <summary>
        /// Builds the model's bones under <paramref name="parent"/> (rest pose: identity rotations, at the delivered
        /// joints) and its skinned renderer, the mesh <paramref name="meshId"/> (default: the model's own). Null if the
        /// model isn't baked.
        /// </summary>
        public static Transform[] Build(ModelData m, Transform parent, out SkinnedMeshRenderer renderer, string meshId = null)
        {
            renderer = null;
            if (m == null) return null;
            var mesh = LoadMesh(meshId ?? m.id);
            if (mesh == null || m.bones == null || m.joints == null) return null;
            var root = new GameObject(m.id).transform;
            root.SetParent(parent, false);
            var bones = new Transform[m.bones.Length];
            // Every bone first: a parent can come later in the list (the shoulders after the arms).
            for (var i = 0; i < bones.Length; i++) bones[i] = new GameObject(m.bones[i]).transform;
            for (var i = 0; i < bones.Length; i++)
            {
                var p = m.parents[i];
                bones[i].SetParent(p < 0 ? root : bones[p], false);
                bones[i].localPosition = m.joints[i] - (p < 0 ? Vector3.zero : m.joints[p]);
            }
            var skin = new GameObject("Skin");
            skin.transform.SetParent(root, false);
            renderer = skin.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.sharedMaterial = MaterialFor(m);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.quality = SkinQuality.Bone4;
            // The body moves inside these bounds while animating (arms reach, legs swing, a long animal's tail).
            var size = Mathf.Max(m.height, mesh.bounds.size.z) * 1.4f;
            renderer.localBounds = new Bounds(new Vector3(0f, m.height * 0.5f, 0f), Vector3.one * size);
            return bones;
        }
    }
}
