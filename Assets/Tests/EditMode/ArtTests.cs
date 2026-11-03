using NUnit.Framework;
using UnityEngine;
using ZombieGame.Art;

namespace ZombieGame.Tests
{
    /// <summary>The built-in map art (owner, 2026-10-02: Claude makes the map graphics).</summary>
    public class ArtTests
    {
        [Test]
        public void PosedCarsRestOnTheGroundWithTheirPosedFootprint()
        {
            // Owner, 2026-10-04: overturned, tilted and crashed cars.
            var car = ArtLibrary.Get("car");
            var def = PackProps.Get("car");
            Assert.IsNotNull(car);
            var upright = new ZombieGame.MapDecoration { id = "car" };
            var side = new ZombieGame.MapDecoration { id = "car", roll = 90f };
            var upsideDown = new ZombieGame.MapDecoration { id = "car", roll = 180f };
            Assert.AreEqual(car.Footprint, PackProps.Footprint(car, upright));
            // On its side the car is as wide as it was tall, and taller than before.
            Assert.Less(PackProps.Footprint(car, side).y, car.Footprint.y);
            Assert.Greater(PackProps.Height(car, side), car.Height);
            // Upside down it keeps its footprint and is lifted by its own height to rest on its roof.
            Assert.AreEqual(car.Footprint.y, PackProps.Footprint(car, upsideDown).y, 0.01f);
            Assert.AreEqual(car.Height, PackProps.PosedBounds(def, 180f, 0f).lift, 0.05f);
        }

        [Test]
        public void EveryPieceBuilds()
        {
            foreach (var piece in ArtLibrary.Pieces.Values)
            {
                var kit = new MeshKit();
                piece.Build(kit, 7);
                Assert.Greater(kit.VertexCount, 0, piece.Id);
                if (piece.Kind == ArtLibrary.Kind.Prop) Assert.Greater(piece.Height, 0f, piece.Id);
            }
        }

        [Test]
        public void ThereAreObstaclesBushesAndDecorations()
        {
            Assert.GreaterOrEqual(ArtLibrary.OfKind(ArtLibrary.Kind.Prop).Count, 10);
            Assert.GreaterOrEqual(ArtLibrary.OfKind(ArtLibrary.Kind.Decoration).Count, 10);
            Assert.IsTrue(ArtLibrary.Get("bush").Bush);
            Assert.IsFalse(ArtLibrary.Get("crate").Bush);
        }

        [Test]
        public void BlocksOnlyBuildFacesTheCameraSees()
        {
            var map = StageMap.Blank("m", 8, 8);
            map[3, 3] = Tile.Wall;
            map[3, 2] = Tile.Wall;
            map[5, 5] = Tile.Cliff;
            map[1, 5] = Tile.Bush;
            map[6, 2] = Tile.Cover;
            var kit = new MeshKit();
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++) BlockArt.Build(kit, map, x, z);
            Assert.Greater(kit.VertexCount, 0);
            var before = kit.VertexCount;
            kit.RemoveHiddenFaces();
            var mesh = kit.ToMesh("t");
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (var i = 0; i < t.Length; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                // Owner, 2026-10-07: faces towards the sun stay too (they cast the shadows).
                Assert.IsTrue(MeshCull.Keep(n), "every remaining triangle faces the camera or the sun");
            }
            Assert.LessOrEqual(mesh.vertexCount, before);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void EachBaseGroundHasATexture()
        {
            foreach (var p in BaseGround.Presets) Assert.IsNotNull(BaseGround.TextureFor(p.Id), p.Id);
        }
    }
}
