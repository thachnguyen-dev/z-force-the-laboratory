using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// One prop or decoration opened in the Unity scene by the Scene Stage Editor (owner, 2026-10-04: add, turn and
    /// mirror map objects directly in the scene). Its position and turn are the object's own transform; everything else
    /// is kept here and written back to the stage file on save. The visual is a child, so clicking it selects this.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class StagePiece : MonoBehaviour
    {
        public string id;
        public bool decoration;
        public float scale = 1f;
        public bool flip;
        /// <summary>0 = colour chosen from the position, 1 = base colours, 2 = "_v1"…</summary>
        public int variant;
        public float roll, pitch;

        public MapDecoration ToRecord()
        {
            var p = transform.position;
            // Hiding grass (owner, 2026-10-05): always on a cell centre, never turned or mirrored.
            if (Art.ArtLibrary.Get(id)?.Bush ?? false)
                return new MapDecoration { id = id, x = Mathf.Floor(p.x) + 0.5f, z = Mathf.Floor(p.z) + 0.5f, scale = scale };
            return new MapDecoration
            {
                id = id, x = Mathf.Round(p.x * 100f) / 100f, z = Mathf.Round(p.z * 100f) / 100f,
                rotation = Mathf.Round(Mathf.Repeat(transform.eulerAngles.y, 360f) * 10f) / 10f,
                scale = scale, flip = flip, variant = variant, roll = roll, pitch = pitch,
            };
        }
    }
}
