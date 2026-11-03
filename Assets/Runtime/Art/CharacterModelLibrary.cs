using System;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The owner's delivered character models as the game uses them (owner, 2026-10-07: connected to the game). Written
    /// by the editor bake (EditorTools.CharacterBake) to Resources/Characters/Library.asset, beside each model's baked
    /// mesh (Resources/Characters/{id}.asset): its skeleton (the delivered bones, fingers merged into the hands), its
    /// camouflage and, for the playable characters, the approved firing poses.
    /// </summary>
    public class CharacterModelLibrary : ScriptableObject
    {
        public const string Resource = "Characters/Library";

        public ModelData[] models = new ModelData[0];

        [Serializable]
        public class ModelData
        {
            public string id;
            /// <summary>The playable character that wears it (empty for zombies).</summary>
            public string character;
            public bool quadruped;
            /// <summary>Bones in the rig's order (CharacterRig.Bone or CharacterRig.Quad), their parents (-1 = the root).</summary>
            public string[] bones;
            public int[] parents;
            /// <summary>Each bone's joint at rest (T-pose or standing), in the model's space in metres (feet at 0, facing +z).</summary>
            public Vector3[] joints;
            /// <summary>The model's height as baked (m): zombies are scaled to their type's height in the game.</summary>
            public float height;
            /// <summary>The branch camouflage texture (Resources/Art/Ground), or empty.</summary>
            public string camo;
            public HoldData[] holds = new HoldData[0];
        }

        /// <summary>
        /// A firing pose (owner, 2026-10-06/07: firing poses only, verified on the real mesh), as the bake read it from the
        /// editor's hold solver: every bone's rotation in the model's space (identity = rest), how far the whole body is
        /// turned so the gun points straight ahead, and the gun's place relative to the right hand.
        /// </summary>
        [Serializable]
        public class HoldData
        {
            /// <summary>The held model (ar_gun, shotgun, sniper_gun, smg_gun, pistol).</summary>
            public string weapon;
            public float bodyTurn;
            public Quaternion[] rotations;
            public Vector3 gunPosition;
            public Quaternion gunRotation;
        }

        private static CharacterModelLibrary _instance;
        private static bool _loaded;

        public static CharacterModelLibrary Instance
        {
            get
            {
                if (!_loaded)
                {
                    _instance = Resources.Load<CharacterModelLibrary>(Resource);
                    _loaded = true;
                }
                return _instance;
            }
        }

        /// <summary>Forgets the loaded library (the editor bake writes a new one).</summary>
        public static void Reload() => _loaded = false;

        public static ModelData Find(string id)
        {
            var lib = Instance;
            if (lib == null) return null;
            foreach (var m in lib.models)
                if (m.id == id) return m;
            return null;
        }

        public static ModelData ForCharacter(string character)
        {
            var lib = Instance;
            if (lib == null || string.IsNullOrEmpty(character)) return null;
            foreach (var m in lib.models)
                if (m.character == character) return m;
            return null;
        }
    }
}
