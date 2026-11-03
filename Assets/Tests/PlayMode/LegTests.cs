using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZombieGame.Art;
using Bone = ZombieGame.Art.CharacterRig.Bone;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-08: the legs straight and close together, the feet parallel, stepping straight. Every playable
    /// character, a long gun in hand, walking forward and backward: across the walking direction the feet stay at most
    /// CharacterPuppet.FeetSpan of the hip joints' spacing apart, and each foot keeps its line (it doesn't step out).
    /// </summary>
    public class LegTests
    {
        private GameSession _session;

        /// <summary>How far (m) the feet may sit on average ahead of or behind the hips over a walk.</summary>
        public const float Centred = 0.06f;

        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FeetStayCloseAndStepStraight()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var report = new System.Text.StringBuilder();
            var problems = new System.Collections.Generic.List<string>();
            foreach (var character in _session.Tuning.characters)
            {
                _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), character, 31);
                for (var i = 0; i < 3; i++) yield return null;
                var player = _session.LocalSurvivor;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
                player.TryPickup(LootItem.SpawnLongGun(player.transform.position, _session.Tuning.Firearm(WeaponId.AR), 30));
                player.Select(Slot.LongGun);
                var puppet = player.GetComponentInChildren<CharacterPuppet>();
                Assert.IsNotNull(puppet, character.id);
                var input = new Scripted { Value = new CharacterInput { GrenadeDistance = -1f } };
                player.InputSource = input;
                foreach (var (way, move) in new[] { ("forward", new Vector2(0f, -1f)), ("backward", new Vector2(0f, 1f)) })
                {
                    input.Value = new CharacterInput { Move = move, Aim = new Vector2(0f, -1f), GrenadeDistance = -1f };
                    yield return new WaitForSeconds(0.8f);
                    var start = player.transform.position;
                    var across = Vector3.right;
                    float hipSpan = 0f, widest = 0f, sumAhead = 0f, minAhead = float.MaxValue, maxAhead = float.MinValue;
                    float minL = float.MaxValue, maxL = float.MinValue, minR = float.MaxValue, maxR = float.MinValue;
                    for (var f = 0; f < 60; f++)
                    {
                        yield return null;
                        var c = player.transform.position;
                        float A(Bone b) => Vector3.Dot(puppet.BoneOf(b).position - c, across);
                        hipSpan = Mathf.Max(hipSpan, Mathf.Abs(A(Bone.UpperLegR) - A(Bone.UpperLegL)));
                        float l = A(Bone.FootL), r = A(Bone.FootR);
                        widest = Mathf.Max(widest, Mathf.Abs(r - l));
                        // Where the feet are along the way the body faces, from under the hip joints.
                        var facing = player.transform.forward;
                        var hips = (puppet.BoneOf(Bone.UpperLegL).position + puppet.BoneOf(Bone.UpperLegR).position) * 0.5f;
                        foreach (var foot in new[] { Bone.FootL, Bone.FootR })
                        {
                            var ahead = Vector3.Dot(puppet.BoneOf(foot).position - hips, facing);
                            sumAhead += ahead;
                            minAhead = Mathf.Min(minAhead, ahead);
                            maxAhead = Mathf.Max(maxAhead, ahead);
                        }
                        minL = Mathf.Min(minL, l); maxL = Mathf.Max(maxL, l);
                        minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                    }
                    var drift = Mathf.Max(maxL - minL, maxR - minR);
                    var walked = (player.transform.position - start).magnitude;
                    var meanAhead = sumAhead / 120f;
                    report.AppendLine($"{character.id} {way}: feet on average {meanAhead * 100f:0.0} cm ahead of the hips (from {minAhead * 100f:0} to {maxAhead * 100f:0} cm)");
                    if (Mathf.Abs(meanAhead) > LegTests.Centred) problems.Add($"{character.id} {way}: the feet are on average {meanAhead * 100f:0.0} cm off the hips");
                    report.AppendLine($"{character.id} {way}: walked {walked:0.00} m, state {_session.State}, held {player.Selected}, gait weight {puppet.Gait.Weight:0.00}, boarded {player.Boarded}");
                    if (walked < 1f) problems.Add($"{character.id} {way}: didn't walk ({walked:0.00} m)");
                    report.AppendLine($"{character.id} {way}: hip joints {hipSpan:0.000} m apart, feet at most {widest:0.000} m ({widest / Mathf.Max(0.001f, hipSpan):0%}), sideways drift {drift * 100f:0.0} cm");
                    if (widest > Mathf.Max(hipSpan * CharacterPuppet.FeetSpan, CharacterPuppet.FeetApart(CharacterModelLibrary.ForCharacter(character.id))) + 0.02f) problems.Add($"{character.id} {way}: feet {widest:0.000} m apart, hips {hipSpan:0.000} m");
                    if (drift > 0.015f) problems.Add($"{character.id} {way}: a foot drifts {drift * 100f:0.0} cm sideways");
                }
                {
                    var model = CharacterModelLibrary.ForCharacter(character.id);
                    var boots = CharacterPuppet.BootsOf(model);
                    report.AppendLine($"{character.id}: boots {boots[0].Width * 100f:0} and {boots[1].Width * 100f:0} cm wide, toes turned {boots[0].ToeYaw:0.0}° and {boots[1].ToeYaw:0.0}° in the model (straightened), feet put {CharacterPuppet.FeetApart(model) * 100f:0} cm apart");
                    if (CharacterPuppet.FeetApart(model) < Mathf.Max(boots[0].Width, boots[1].Width) + 0.02f) problems.Add($"{character.id}: the boots would touch");
                }
                player.InputSource = null;
                _session.GoTo(SessionState.MainMenu);
                yield return null;
            }
            Debug.Log("[ZOMBIE-LEGS]\n" + report);
            Assert.IsEmpty(problems, string.Join("\n", problems) + "\n" + report);
        }
    }
}
