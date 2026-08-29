using System;
using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>イベント音の発火表 (Step 10-4)。</summary>
    public sealed class AudioEventsTests
    {
        /// <summary>箱の定義の要求可能距離 (Step 13-1a)。**グローバル定数ではなく定義から読む。**</summary>
        static readonly double BoxRange = StationCatalog.Box().RequestRange;

        static readonly DockingState[] AllStates =
            (DockingState[])Enum.GetValues(typeof(DockingState));

        /// <summary>鳴らす遷移。**ここに無い組み合わせは全て None。**</summary>
        static readonly (DockingState from, DockingState to, SoundId sound)[] Expected =
        {
            (DockingState.Approaching, DockingState.DockRequested, SoundId.UiConfirm),
            (DockingState.Docking, DockingState.Docked, SoundId.DockImpact),
            (DockingState.Docked, DockingState.Undocking, SoundId.Undock),
        };

        [Test]
        public void ドッキング状態は6つ()
        {
            Assert.That(AllStates.Length, Is.EqualTo(6));
        }

        [Test]
        public void 鳴らす遷移が仕様どおり()
        {
            foreach ((DockingState from, DockingState to, SoundId sound) in Expected)
            {
                Assert.That(AudioEvents.OnTransition(from, to), Is.EqualTo(sound),
                            $"{from} -> {to}");
            }
        }

        [Test]
        public void 表に無い遷移は鳴らない()
        {
            var expected = new HashSet<(DockingState, DockingState)>();
            foreach ((DockingState from, DockingState to, SoundId _) in Expected)
            {
                expected.Add((from, to));
            }

            // **6 x 6 の全組み合わせを見る。** 取りこぼしを残さない。
            foreach (DockingState from in AllStates)
            {
                foreach (DockingState to in AllStates)
                {
                    if (expected.Contains((from, to)))
                    {
                        continue;
                    }

                    Assert.That(AudioEvents.OnTransition(from, to), Is.EqualTo(SoundId.None),
                                $"{from} -> {to} が鳴っている");
                }
            }
        }

        [Test]
        public void 状態が変わらなければ鳴らない()
        {
            // **Docking は 5 秒間毎フレーム真になる。** ここで鳴ると連続発火する。
            foreach (DockingState state in AllStates)
            {
                Assert.That(AudioEvents.OnTransition(state, state), Is.EqualTo(SoundId.None),
                            state.ToString());
            }
        }

        [Test]
        public void 接岸の開始ではなく完了で鳴らす()
        {
            // DockRequested -> Docking は補間の開始でしかない。
            Assert.That(AudioEvents.OnTransition(DockingState.DockRequested, DockingState.Docking),
                        Is.EqualTo(SoundId.None), "接岸の開始で金属音が鳴っている");
            Assert.That(AudioEvents.OnTransition(DockingState.Docking, DockingState.Docked),
                        Is.EqualTo(SoundId.DockImpact));
        }

        [Test]
        public void 出港は操作の受理で鳴らし離れきったときには鳴らさない()
        {
            // Undocking -> Free は 5 秒後。スイッチ音には遅すぎる。
            Assert.That(AudioEvents.OnTransition(DockingState.Docked, DockingState.Undocking),
                        Is.EqualTo(SoundId.Undock));
            Assert.That(AudioEvents.OnTransition(DockingState.Undocking, DockingState.Free),
                        Is.EqualTo(SoundId.None), "離れきったときに鳴っている");
        }

        // ---- 最小間隔 ----

        [Test]
        public void 警告以外は重ねてよい()
        {
            foreach (SoundId sound in new[]
                     { SoundId.DockImpact, SoundId.Undock, SoundId.UiSelect, SoundId.UiConfirm })
            {
                Assert.That(AudioEvents.CanPlay(sound, 0.0), Is.True, sound.ToString());
            }
        }

        [Test]
        public void 警告は最小間隔を守る()
        {
            Assert.That(AudioEvents.CanPlay(SoundId.Warning, -1.0), Is.True, "初回は鳴る");
            Assert.That(AudioEvents.CanPlay(SoundId.Warning, 0.0), Is.False, "同フレームで鳴った");
            Assert.That(AudioEvents.CanPlay(SoundId.Warning, 0.14), Is.False);
            Assert.That(AudioEvents.CanPlay(SoundId.Warning, AudioEvents.WarningMinIntervalSeconds),
                        Is.True);
            Assert.That(AudioEvents.CanPlay(SoundId.Warning, 1.0), Is.True);
        }

        [Test]
        public void 最小間隔は0対15秒()
        {
            Assert.That(AudioEvents.WarningMinIntervalSeconds, Is.EqualTo(0.15).Within(1e-9));
        }

        // ---- 拒否の検出 ----

        [Test]
        public void 要求を押さなければ拒否にならない()
        {
            var solver = new DockingSolver();
            solver.Step(1000.0, 0.0, 0.0, requestPressed: false, undockPressed: false, 0.016, BoxRange);

            Assert.That(solver.LastRequestRejected, Is.False);
            Assert.That(solver.LastRejection, Is.Not.Empty,
                        "LastRejection は押していなくても中身が入る（だから音には使えない）");
        }

        [Test]
        public void 圏外で要求すると拒否になる()
        {
            var solver = new DockingSolver();
            solver.Step(1000.0, 0.0, 0.0, requestPressed: true, undockPressed: false, 0.016, BoxRange);

            Assert.That(solver.State, Is.EqualTo(DockingState.Free));
            Assert.That(solver.LastRequestRejected, Is.True);
        }

        [Test]
        public void 受理されたときは拒否にならない()
        {
            var solver = new DockingSolver();

            // 圏内に入って Approaching へ。
            solver.Step(10.0, 0.0, 0.0, false, false, 0.016, BoxRange);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching));

            solver.Step(10.0, 0.0, 0.0, requestPressed: true, undockPressed: false, 0.016, BoxRange);

            Assert.That(solver.State, Is.EqualTo(DockingState.DockRequested));
            Assert.That(solver.LastRequestRejected, Is.False, "受理されたのに拒否になっている");
        }

        [Test]
        public void 拒否は押したフレームだけ真()
        {
            var solver = new DockingSolver();
            solver.Step(1000.0, 0.0, 0.0, requestPressed: true, undockPressed: false, 0.016, BoxRange);
            Assert.That(solver.LastRequestRejected, Is.True);

            // 押していない次のフレームで false に戻ること。
            solver.Step(1000.0, 0.0, 0.0, requestPressed: false, undockPressed: false, 0.016, BoxRange);
            Assert.That(solver.LastRequestRejected, Is.False, "押していないのに真のまま");
        }
    }
}
