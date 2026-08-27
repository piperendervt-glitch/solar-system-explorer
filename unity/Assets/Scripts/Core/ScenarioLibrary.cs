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

            // 惑星の見た目 (Step 8-2)。太陽の向きを変えて 3 通り。
            // キー割り当てを増やさないよう、角度違いは別シナリオにして F2/F3 で送る。
            list.Add(CreateEarthClose(model, EarthDayName, SunSide.Day));
            list.Add(CreateEarthClose(model, EarthTerminatorName, SunSide.Terminator));
            list.Add(CreateEarthClose(model, EarthNightName, SunSide.Night));

            // LOD と実スケール引き渡しの確認 (Step 2 / 3b から移設)。
            list.Add(CreateLod(model, "lod-from-earth", 2.0e4, fromEarth: true,
                "地球近傍 (地球から 2e4 units) から火星方向"));
            list.Add(CreateLod(model, "lod-midpoint", SolarSystemModel.EarthToMarsKm * 0.5, fromEarth: true,
                "地球-火星の中間点"));
            list.Add(CreateLod(model, "lod-mars-5e4", 5.0e4, fromEarth: false,
                "火星まで 5e4 units (引き渡し帯の入口)"));
            list.Add(CreateLod(model, "lod-mars-2e4", 2.0e4, fromEarth: false,
                "火星まで 2e4 units (メッシュ切替後)"));

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

        public const string EarthDayName = "earth-close-day";
        public const string EarthTerminatorName = "earth-close-terminator";
        public const string EarthNightName = "earth-close-night";

        /// <summary>地球を見る位置に対する太陽の向き。</summary>
        public enum SunSide
        {
            /// <summary>昼側正面。光は視線と同じ向きに進む。</summary>
            Day,

            /// <summary>明暗境界線が画面中央。光は視線と直交する。</summary>
            Terminator,

            /// <summary>夜側正面。光は視線と逆向きに進む。</summary>
            Night,
        }

        /// <summary>観測者を置く距離 (地球半径の倍数)。円盤が画面いっぱいになる。</summary>
        public const double EarthCloseRadii = 2.5;

        /// <summary>
        /// 地球に寄って表面の作り込みを見る (Step 8-2)。
        /// 太陽の向きは SunDirectionOverride で決め打ちする。
        /// </summary>
        static Scenario CreateEarthClose(SolarSystemModel model, string name, SunSide side)
        {
            // 観測者は「地球から見て +X 方向」に置き、地球を見る。
            var offset = new Vec3d(1.0, 0.0, 0.0);
            Vec3d position = model.Earth.AbsolutePosition
                             + offset * (SolarSystemModel.EarthRadiusKm * EarthCloseRadii);

            // 光の進む向き。視線は -offset なので:
            //   Day        : 光も -offset に進む = 背後から照らす = 昼側が見える
            //   Night      : 光は +offset に進む = 正面から来る = 夜側が見える
            //   Terminator : 光は視線と直交 = 明暗境界線が中央に来る
            Vec3d sun;
            string[] checkPoints;
            switch (side)
            {
                case SunSide.Day:
                    sun = offset * -1.0;
                    checkPoints = new[]
                    {
                        "海が鏡面で光り、陸との質感差が出ている",
                        "円盤の縁が青くにじむ (大気)",
                        "街灯りは見えない",
                    };
                    break;

                case SunSide.Night:
                    sun = offset;
                    checkPoints = new[]
                    {
                        "街灯りだけが見える",
                        "地形の陰影は見えない",
                        "大洋の中央に孤立した輝点が無い",
                    };
                    break;

                default:
                    sun = new Vec3d(0.0, 0.0, 1.0);
                    checkPoints = new[]
                    {
                        "明暗境界線が画面中央にある",
                        "街灯りが段差なく立ち上がる",
                        "夜側の縁は暗く、昼側の縁だけ青い",
                    };
                    break;
            }

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = model.Earth.AbsolutePosition,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = sun,
            };

            return new Scenario(name, $"地球へ {EarthCloseRadii} 半径まで寄る ({side})",
                                start, checkPoints);
        }

        /// <summary>
        /// LOD と実スケール引き渡しの確認 (Step 2 / 3b の視点を移設)。
        /// 旧 VerifyCapture の 01〜04 に対応する。
        /// </summary>
        static Scenario CreateLod(SolarSystemModel model, string name, double distance,
                                  bool fromEarth, string description)
        {
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d mars = model.Mars.AbsolutePosition;
            Vec3d towardMars = (mars - earth).Normalized;

            Vec3d position = fromEarth
                ? earth + towardMars * distance
                : mars - towardMars * distance;

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = mars,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 1, // 火星ステーション
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = null,
            };

            return new Scenario(name, description, start, new[]
            {
                "火星が視界の中央にある",
                "光点とメッシュの切替が破綻していない",
            });
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
