using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZombieGame.Art;

namespace ZombieGame.Tests
{
    /// <summary>Free-placed props (owner, 2026-10-02: maps no longer have to follow the grid).</summary>
    public class PropTests
    {
        private GameObject _root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            StageGrid.Map = null;
            StageGrid.HasSolidProps = false;
            StageGrid.BushMask = null;
            yield return null;
        }

        /// <summary>Owner, 2026-10-05: leaves and paper don't stay still; a gust moves them, then they lie flat again.</summary>
        [UnityTest]
        public IEnumerator WindBlowsLooseBitsThenTheySettle()
        {
            _root = new GameObject("Wind test");
            StageGrid.Map = StageMap.Blank("Wind", 40, 40);
            WindBlown.Place(_root.transform, new MapDecoration { id = "wind_paper", x = 20f, z = 20f, scale = 1f }, 5);
            WindBlown.Place(_root.transform, new MapDecoration { id = "wind_leaves", x = 22f, z = 20f, scale = 1f }, 9);
            var bits = _root.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(bits.Length, 5);
            var before = new Vector3[bits.Length];
            for (var i = 0; i < bits.Length; i++) before[i] = bits[i].transform.position;
            yield return null;
            WindBlown.GustNow(true);
            yield return new WaitForSeconds(3.5f);
            var moved = 0;
            for (var i = 0; i < bits.Length; i++)
            {
                var p = bits[i].transform.position;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(before[i].x, before[i].z)) > 0.3f) moved++;
                Assert.Less(p.y, 0.05f, "a bit is still in the air after the gust");
                Assert.LessOrEqual(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(before[i].x, before[i].z)), WindBlown.Leash + 0.01f);
            }
            Assert.Greater(moved, bits.Length / 2, "the gust should have blown most bits along");
        }

        private StageMap Build()
        {
            var map = StageMap.Blank("props", 20, 20);
            map.props.Add(new MapDecoration { id = "crate", x = 10.3f, z = 5.5f, rotation = 20f, scale = 1f });
            map.props.Add(new MapDecoration { id = "bush", x = 5f, z = 15f, scale = 1f });
            _root = new GameObject("Props test");
            StageGrid.Map = map;
            PropLayer.BuildProps(map, _root.transform);
            Physics.SyncTransforms();
            return map;
        }

        [UnityTest]
        public IEnumerator PropsBlockSightAndWalking()
        {
            Build();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(StageGrid.SightBlocked(new Vector3(6f, 1f, 5.5f), new Vector3(15f, 1f, 5.5f)), "a crate between them");
            Assert.IsFalse(StageGrid.SightBlocked(new Vector3(6f, 1f, 9f), new Vector3(15f, 1f, 9f)), "nothing between them");
            Assert.IsTrue(Physics.CheckSphere(new Vector3(10.3f, 0.8f, 5.5f), 0.3f, Layers.ObstacleMask), "a collider to walk into");
        }

        [UnityTest]
        public IEnumerator BushPropsHideWhoeverIsInside()
        {
            Build();
            yield return null;
            Assert.IsTrue(StageGrid.InBush(new Vector3(5.2f, 0f, 15.1f)));
            Assert.IsFalse(StageGrid.InBush(new Vector3(9f, 0f, 15f)));
            Assert.IsTrue(StageGrid.HiddenByBush(new Vector3(12f, 0f, 15f), new Vector3(5f, 0f, 15f)));
            Assert.IsFalse(Physics.CheckSphere(new Vector3(5f, 0.8f, 15f), 0.3f, Layers.ObstacleMask), "bushes have no collider");
        }
    }
}
