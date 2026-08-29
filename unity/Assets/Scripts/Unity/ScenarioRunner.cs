using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 検証シナリオの実行 (Step 8-0)。
    ///
    /// 起動引数 `-scenario &lt;name&gt;` が無ければ**何もしない**。通常プレイの挙動は変わらない。
    ///
    ///   SolarSystemExplorer.exe -scenario harness-selftest
    ///
    /// 定義は Core の ScenarioLibrary。batchmode のスクショ撮影も同じ定義を読む。
    /// Update() を持たない (決定 D-1)。切替は UniverseRoot.Tick から呼ばれる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioRunner : MonoBehaviour
    {
        public const string ScenarioArg = "-scenario";

        IReadOnlyList<Scenario> _all;
        int _index = -1;

        /// <summary>シナリオ実行中か。引数が無ければ false。</summary>
        public bool IsActive => _index >= 0 && _all != null && _index < _all.Count;

        public Scenario Current => IsActive ? _all[_index] : null;

        public int Index => _index;

        public int Count => _all != null ? _all.Count : 0;

        /// <summary>起動引数で指定された名前。無ければ null。</summary>
        public static string RequestedName() => StandaloneCapture.ArgValue(ScenarioArg);

        /// <summary>
        /// 定義を読み込み、起動引数のシナリオがあれば選ぶ。
        /// 引数が無ければ非活性のまま返る。
        /// </summary>
        public void Initialize(SolarSystemModel model)
        {
            _all = ScenarioLibrary.Create(model);

            string requested = RequestedName();
            if (string.IsNullOrEmpty(requested))
            {
                _index = -1;
                return;
            }

            int found = ScenarioLibrary.IndexOf(_all, requested);
            if (found < 0)
            {
                Debug.LogWarning(
                    $"[ScenarioRunner] シナリオ '{requested}' が無い。通常プレイで起動する。" +
                    $" 定義済み: {string.Join(", ", Names())}");
                _index = -1;
                return;
            }

            _index = found;
        }

        /// <summary>テストや batchmode から名前で選ぶ。無ければ false。</summary>
        public bool Select(SolarSystemModel model, string name)
        {
            if (_all == null)
            {
                _all = ScenarioLibrary.Create(model);
            }

            int found = ScenarioLibrary.IndexOf(_all, name);
            if (found < 0)
            {
                return false;
            }

            _index = found;
            return true;
        }

        public string[] Names()
        {
            if (_all == null)
            {
                return new string[0];
            }

            var names = new string[_all.Count];
            for (int i = 0; i < _all.Count; i++)
            {
                names[i] = _all[i].Name;
            }

            return names;
        }

        /// <summary>次へ / 前へ。端は巻き戻る。非活性のときは何もしない。</summary>
        public void Step(int delta)
        {
            if (!IsActive || _all.Count == 0)
            {
                return;
            }

            _index = ((_index + delta) % _all.Count + _all.Count) % _all.Count;
        }

        /// <summary>
        /// 現在のシナリオの初期状態を場面へ適用する。
        /// 位置・姿勢・目標・時刻・カメラ設定・HUD の初期状態。
        /// </summary>
        public void Apply(UniverseRoot root, ShipRig rig, CameraStackController stack,
                          DebugOverlay overlay)
        {
            Scenario scenario = Current;
            if (scenario == null || root == null)
            {
                return;
            }

            ScenarioStart start = scenario.Start;

            if (rig != null)
            {
                rig.SetTargetIndex(start.TargetStationIndex);
            }

            root.PlaceObserver(start.Position);

            if (rig != null && rig.ShipTransform != null)
            {
                Vec3d forward = start.LookAt - start.Position;
                var f = new Vector3((float)forward.X, (float)forward.Y, (float)forward.Z);
                var up = new Vector3((float)start.Up.X, (float)start.Up.Y, (float)start.Up.Z);
                if (f.sqrMagnitude > 0f && up.sqrMagnitude > 0f)
                {
                    rig.ShipTransform.rotation = Quaternion.LookRotation(f, up);
                }
            }

            // **動きの状態を読む (Step 13-0b)。**
            // `PlaceObserver` が速度を 0 にした後に入れ直す。
            // **未設定なら `ScenarioMotion` 側が例外を投げる。**
            // 書き忘れたシナリオを静止として静かに通さないための経路で、
            // 「設定を書いたこと」ではなく「読まれること」がこの塞ぎの本体。
            ApplyMotion(root, start);

            root.SetElapsedSeconds(start.ElapsedSeconds);
            root.SetSunDirectionOverride(start.SunDirectionOverride);

            if (stack != null)
            {
                stack.SetVerticalFov((float)start.VerticalFovDegrees);
            }

            if (overlay != null)
            {
                // 起動引数 -debugHud は明示的な指定なので、シナリオの既定より優先する。
                overlay.Visible = start.DebugHudVisible || DebugOverlay.HasDebugHudArg();
                overlay.BindScenario(this);
            }

            // **トグルだけ既定へ戻す。数値は保持する (Step 8-0b)。**
            // earth-close-day で決めた値を terminator や night でも確かめるため。
            root.DebugPanel?.ResetTogglesForScenario();

            Debug.Log($"[ScenarioRunner] '{scenario.Name}' を適用 ({_index + 1}/{Count}): {scenario.Description}");
        }

        /// <summary>
        /// 場面の動きを船に入れる。**静止 (β=0) なら何も起きない**
        /// （`PlaceObserver` が既に速度を 0 にしている）。
        ///
        /// 向きは視線方向。`LookAt` が `Position` と同じなら動かしようがないので
        /// 巡航は成立しない（例外にする）。
        /// </summary>
        static void ApplyMotion(UniverseRoot root, ScenarioStart start)
        {
            // **ここで読むことに意味がある。** 未設定なら例外が出る。
            double kmPerSec = start.Motion.SpeedKmPerSec;
            if (kmPerSec <= 0.0)
            {
                return;
            }

            Vec3d direction = (start.LookAt - start.Position).Normalized;
            if (direction.SqrMagnitude <= 0.0)
            {
                throw new System.InvalidOperationException(
                    "巡航の向きが決まらない（LookAt と Position が同じ）");
            }

            root.Ship.SetVelocity(direction * kmPerSec);
        }
    }
}
