using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 検証シナリオの定義集 (Step 8-0)。
    ///
    /// **モデルを受け取って絶対座標を解決する。** リテラル座標を Core に固定せず、
    /// 「地球ステーションのポートから 3 units 手前」のようにモデル由来で書ける。
    ///
    /// 実行時 (ScenarioRunner) と batchmode のスクショ撮影 (ScenarioCapture) の
    /// 両方がここを呼ぶので、定義は 1 箇所で済む。
    /// </summary>
    public static class ScenarioLibrary
    {
        /// <summary>8-0 の自己診断用。以降の Step でシナリオを増やす。</summary>
        public const string SelfTestName = "harness-selftest";

        public static IReadOnlyList<Scenario> Create(SolarSystemModel model)
        {
            var list = new List<Scenario>();
            if (model == null || model.Stations == null || model.Stations.Count == 0)
            {
                return list;
            }

            list.Add(CreateSelfTest(model));
            return list;
        }

        /// <summary>名前で引く。無ければ null を返す。**例外は投げない。**</summary>
        public static Scenario Find(IReadOnlyList<Scenario> all, string name)
        {
            if (all == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Name, name, System.StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }

        /// <summary>名前の番号。無ければ -1。</summary>
        public static int IndexOf(IReadOnlyList<Scenario> all, string name)
        {
            if (all == null || string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Name, name, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// ハーネス自体の自己診断。
        /// 地球ステーションを背にして 3 units 進んだ地点から地球を見る。
        /// </summary>
        static Scenario CreateSelfTest(SolarSystemModel model)
        {
            SpaceStation earthStation = model.Stations[0];

            // **ステーションを背にして地球を見る。**
            // ポート正面や真横に置くとステーション本体が視界を塞ぐ
            // (実測: 428x400 px の矩形として地球の中央に写り込んだ)。
            // 地球へ向かう向きに 3 units 出れば、ステーションは真後ろになる。
            Vec3d towardEarth = (model.Earth.AbsolutePosition - earthStation.AbsolutePosition).Normalized;

            var start = new ScenarioStart
            {
                Position = earthStation.AbsolutePosition + towardEarth * 3.0,
                LookAt = model.Earth.AbsolutePosition,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = null, // 8-0 では使わない
            };

            return new Scenario(
                SelfTestName,
                "地球ステーションを背に 3 units から地球を見る (ハーネスの自己診断)",
                start,
                new[]
                {
                    "地球が視界の中央にあり、ステーションが写り込まない",
                    "計器の 2 行が読める",
                    "F1 でこの表示ごと HUD が消える",
                });
        }
    }
}
