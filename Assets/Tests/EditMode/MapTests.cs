using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    public class MapTests
    {
        private static StageMap Corridor()
        {
            // 10 × 5, a wall column at x = 5 with a gap at z = 4, a bush at (2, 2), cover at (7, 2).
            var map = StageMap.Blank("t", 10, 5);
            for (var z = 0; z < 4; z++) map[5, z] = Tile.Wall;
            map[2, 2] = Tile.Bush;
            map[7, 2] = Tile.Cover;
            return map;
        }

        [Test]
        public void JsonRoundTripKeepsEveryCell()
        {
            var map = Corridor();
            map.Pack();
            var back = StageMap.FromJson(map.ToJson());
            for (var x = 0; x < 10; x++)
            for (var z = 0; z < 5; z++) Assert.AreEqual(map[x, z], back[x, z], $"cell {x},{z}");
            Assert.AreEqual("#", back.rows[4].Substring(5, 1), "row 0 is north: the bottom row z = 0 is the last string");
        }

        [Test]
        public void D051_GridWallsBlockSightAndGapsDont()
        {
            StageGrid.Map = Corridor();
            try
            {
                Assert.IsTrue(StageGrid.SightBlocked(new Vector3(1.5f, 1f, 1.5f), new Vector3(8.5f, 1f, 1.5f)));
                Assert.IsFalse(StageGrid.SightBlocked(new Vector3(1.5f, 1f, 4.5f), new Vector3(8.5f, 1f, 4.5f)), "through the gap");
                Assert.IsTrue(StageGrid.SightBlocked(new Vector3(6.5f, 1f, 2.5f), new Vector3(8.5f, 1f, 2.5f)), "cover blocks sight");
                Assert.IsFalse(StageGrid.SightBlocked(new Vector3(1.5f, 1f, 2.5f), new Vector3(3.5f, 1f, 2.5f)), "a bush doesn't block the line itself");
            }
            finally
            {
                StageGrid.Map = null;
            }
        }

        [Test]
        public void BushHidesFromAfarOnly()
        {
            StageGrid.Map = Corridor();
            try
            {
                var inBush = new Vector3(2.5f, 0f, 2.5f);
                Assert.IsTrue(StageGrid.HiddenByBush(new Vector3(2.5f, 0f, 4.9f + 1f), inBush));
                Assert.IsFalse(StageGrid.HiddenByBush(new Vector3(3.5f, 0f, 3.5f), inBush), "right next to it");
                Assert.IsFalse(StageGrid.HiddenByBush(new Vector3(9f, 0f, 0.5f), new Vector3(1.5f, 0f, 1.5f)), "not in a bush");
            }
            finally
            {
                StageGrid.Map = null;
            }
        }

        [Test]
        public void MergedCollidersCoverEverySolidCell()
        {
            var layout = StageMapBuilder.ToLayout(Corridor());

            Assert.AreEqual(2, layout.Blocks.Count, "the wall column merges into one box, plus the cover");
        }
    }
}
