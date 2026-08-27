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

            // 引き渡し帯の整合 (Step 8-5)。帯 (5e4 -> 3e4) を挟んだ 3 距離。
            list.Add(CreateHandover(model, HandoverOutName, 5.2e4));
            list.Add(CreateHandover(model, HandoverMidName, 4.0e4));
            list.Add(CreateHandover(model, HandoverInName, 2.8e4));

            // 自転 (Step 8-4)。等倍なので動きとしては見えない。
            // **時刻を進めた静止画を 2 枚並べて確認する。** F2/F3 で行き来する。
            list.Add(CreateEarthSpin(model, EarthSpinT0Name, 0.0));
            list.Add(CreateEarthSpin(model, EarthSpinT6hName, 6.0 * 3600.0));

            // フレアの遮蔽の遷移 (Step 9-3a)。
            // 遷移幅は太陽の角直径ぶん (約 0.53 度) しかなく、静止画 1 枚では
            // 「強くなる」が見えない。3 点に分けて F2/F3 で連続して見る。
            list.Add(CreateSunOcclusion(model, SunHiddenName, SunPhase.Hidden));
            list.Add(CreateSunOcclusion(model, SunEmergeName, SunPhase.Emerge));
            list.Add(CreateSunOcclusion(model, SunClearName, SunPhase.Clear));

            // 太陽の HDR 化 (Step 9-1)。眩しさと縁の立ち方は目で見るしかない。
            list.Add(CreateSunFace(model));

            // LOD と実スケール引き渡しの確認 (Step 2 / 3b から移設)。
            list.Add(CreateLod(model, "lod-from-earth", 2.0e4, fromEarth: true,
                "地球近傍 (地球から 2e4 units) から火星方向"));
            // **太陽の LOD 帯の連続性はここで見る (Step 9-1)。**
            // 中間点では太陽が 6.85 px で、光点 α 0.287 / 殻 α 0.713 の
            // 両方が描かれている。静止画 1 枚では跳びが見えないので、
            // earth-close-day と行き来して比べる。
            list.Add(CreateLod(model, "lod-midpoint", SolarSystemModel.EarthToMarsKm * 0.5, fromEarth: true,
                "地球-火星の中間点", new[]
                {
                    "火星が視界の中央にあり、光点とメッシュの切替が破綻していない",
                    "太陽が光点と殻の両方で描かれている (角直径 6.85 px)",
                    "地球近傍 (earth-close-day) と比べて太陽の明るさが跳んでいない",
                }));
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

        public const string HandoverOutName = "earth-handover-5e4";
        public const string HandoverMidName = "earth-handover-4e4";
        public const string HandoverInName = "earth-handover-3e4";

        public const string EarthSpinT0Name = "earth-spin-t0";
        public const string EarthSpinT6hName = "earth-spin-t6h";

        public const string SunHiddenName = "sun-hidden";
        public const string SunEmergeName = "sun-emerge";
        public const string SunClearName = "sun-clear";

        /// <summary>太陽を正面から見る (Step 9-1)。距離は地球近傍。</summary>
        public const string SunFaceName = "sun-face";

        /// <summary>太陽が地球の縁からどれだけ出ているか。</summary>
        public enum SunPhase
        {
            /// <summary>完全に隠れている。遮蔽率 1.0。</summary>
            Hidden,

            /// <summary>縁に半分かかっている。遮蔽率 0.5。</summary>
            Emerge,

            /// <summary>出きっている。遮蔽率 0.0。</summary>
            Clear,
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
        /// 引き渡し帯を挟んだ 3 距離 (Step 8-5)。
        ///
        /// 帯の中央では殻と実スケールが半分ずつ描かれる。**両者の見た目が
        /// 一致していないと、ここで模様が二重像になる。**
        /// </summary>
        static Scenario CreateHandover(SolarSystemModel model, string name, double distance)
        {
            var offset = new Vec3d(1.0, 0.0, 0.0);
            Vec3d position = model.Earth.AbsolutePosition + offset * distance;
            double blend = RealScaleHandoff.Blend(distance);

            string where;
            if (blend <= 0.0)
            {
                where = "帯の外側 (殻だけが描かれる)";
            }
            else if (blend >= 1.0)
            {
                where = "帯の内側 (実スケールだけが描かれる)";
            }
            else
            {
                where = "帯の中央 (殻と実スケールが半分ずつ)";
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
                SunDirectionOverride = offset * -1.0, // 昼側。模様の一致が見やすい
            };

            return new Scenario(
                name,
                $"引き渡し帯 {distance:E1} units — {where}",
                start,
                new[]
                {
                    $"引き渡し率が {blend:F3} になっている",
                    "地表の模様が二重像になっていない",
                    "雲の位置と濃さが飛んでいない",
                });
        }

        /// <summary>
        /// 自転を目で確認するための 1 対 (Step 8-4)。
        ///
        /// **等倍 (SpeedMultiplier = 1.0) では動きとして見えない。**
        /// earth-close (円盤 356 px) で 5 分に 7.8 px、1 秒に 0.026 px しか動かない。
        /// 誇張すると ETA と矛盾するので、代わりに時刻を進めた静止画を並べる。
        /// 6 時間で地表は 90.3 度、雲は 108 度回る。
        /// </summary>
        static Scenario CreateEarthSpin(SolarSystemModel model, string name, double elapsedSeconds)
        {
            // 昼側正面。模様の動きが最も見やすい。
            var offset = new Vec3d(1.0, 0.0, 0.0);
            Vec3d position = model.Earth.AbsolutePosition
                             + offset * (SolarSystemModel.EarthRadiusKm * EarthCloseRadii);

            double hours = elapsedSeconds / 3600.0;
            double surface = BodyRotation.AngleDegrees(elapsedSeconds, model.Earth.RotationPeriodHours);
            double cloud = BodyRotation.AngleDegrees(elapsedSeconds, BodyRotation.EarthCloudPeriodHours);

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = model.Earth.AbsolutePosition,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = elapsedSeconds,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = offset * -1.0, // 昼側
            };

            return new Scenario(
                name,
                $"自転の確認用 (t = {hours:F0} 時間)",
                start,
                new[]
                {
                    $"地表が {surface:F1} 度 回っている",
                    $"雲が {cloud:F1} 度 回っている (地表より速い)",
                    "F2/F3 で 2 枚を行き来して模様の位置を比べる",
                });
        }

        /// <summary>
        /// フレアの遮蔽の遷移を見る (Step 9-3a)。
        ///
        /// **SunDirectionOverride は使わない。** 遮蔽判定が本物の幾何で動くことを見るため、
        /// 実際の太陽位置をそのまま使う。
        ///
        /// **注意: SunDirectionOverride は光の向きだけを差し替える。**
        /// 太陽の位置は動かないので、遮蔽率は常に本物の幾何で決まる。
        /// earth-close-day は陰影こそ昼側だが、実際には太陽が地球の裏にあるため
        /// 遮蔽率 1.000 になる (実測)。フレアはもともと視錐台の外なので実害は無いが、
        /// **override を使ったシナリオでフレアの遮蔽を語ってはいけない。**
        ///
        /// 観測者は地球中心から EarthCloseRadii 倍の球面上に置き、
        /// 「太陽 → 地球」の軸から角度 theta だけずらす。太陽は十分遠いので、
        /// 観測者から見た太陽と地球の角距離はほぼ theta に等しくなる。
        ///   theta = 0            → 遮蔽率 1.0
        ///   theta = ab           → 遮蔽率 0.5 (太陽の中心が地球の縁に乗る)
        ///   theta = ab + as + 余裕 → 遮蔽率 0.0
        /// </summary>
        static Scenario CreateSunOcclusion(SolarSystemModel model, string name, SunPhase phase)
        {
            double distance = SolarSystemModel.EarthRadiusKm * EarthCloseRadii;
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d sun = model.Sun.AbsolutePosition;

            // 太陽から地球へ向かう向き。この先 (地球の裏側) が「完全に隠れる」位置。
            Vec3d axis = (earth - sun).Normalized;
            Vec3d side = PerpendicularTo(axis);

            double bodyAngle = FlareOcclusion.AngularRadius(SolarSystemModel.EarthRadiusKm, distance);
            double sunAngle = FlareOcclusion.AngularRadius(
                SolarSystemModel.SunRadiusKm, Vec3d.Distance(earth, sun));

            double theta;
            string[] checkPoints;
            switch (phase)
            {
                case SunPhase.Hidden:
                    theta = 0.0;
                    checkPoints = new[]
                    {
                        "完全に隠れている間はフレアが出ない (遮蔽率 1.00)",
                        "HUD の FLARE 行が occl 1.00 / int 0.00",
                        "地球の夜側と縁の青はいつもどおり",
                    };
                    break;

                case SunPhase.Emerge:
                    theta = bodyAngle; // 太陽の中心が縁に乗る = 遮蔽率 0.5
                    checkPoints = new[]
                    {
                        "太陽が縁に半分かかり、フレアが弱く出ている (遮蔽率 0.50)",
                        "HUD の FLARE 行が occl 0.50 / int 0.30",
                        "F3 で sun-hidden に戻すとフレアが消える",
                    };
                    break;

                default:
                    theta = bodyAngle + sunAngle * 1.5; // 接触の外側へ余裕を取る
                    checkPoints = new[]
                    {
                        "縁から出きって、フレアが最大 (遮蔽率 0.00)",
                        "HUD の FLARE 行が occl 0.00 / int 0.60",
                        "F3 で sun-emerge に戻すとフレアが弱まる",
                    };
                    break;
            }

            Vec3d dir = axis * System.Math.Cos(theta) + side * System.Math.Sin(theta);
            Vec3d position = earth + dir.Normalized * distance;

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = earth,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = null, // 本物の幾何で判定させる
            };

            return new Scenario(name, $"太陽が地球の縁から出る途中 ({phase})", start, checkPoints);
        }

        /// <summary>
        /// 太陽を正面から見る (Step 9-1)。
        ///
        /// **地球から 1e5 units だけ太陽側に出た位置。** 地球を背にするので
        /// 視界には太陽だけが残る。距離は 1.495e8 units で、角直径は 8.70 px。
        /// 地球の軌道上から見た太陽の本当の大きさで、ここは誇張しない。
        /// </summary>
        static Scenario CreateSunFace(SolarSystemModel model)
        {
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d sun = model.Sun.AbsolutePosition;
            Vec3d towardSun = (sun - earth).Normalized;

            Vec3d position = earth + towardSun * 1.0e5;

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = sun,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                DebugHudVisible = false,
                SunDirectionOverride = null, // 本物の幾何で判定させる
            };

            return new Scenario(SunFaceName, "太陽を正面から見る (地球近傍)", start, new[]
            {
                "太陽が眩しい (bloom で白く滲んでいる)",
                "コロナが円盤の外へ滑らかに広がり、縁で切れていない",
                "縁が中心よりわずかに暗く、模様が潰れきっていない (周辺減光)",
            });
        }

        /// <summary>与えた向きに垂直な単位ベクトルを 1 つ返す。</summary>
        static Vec3d PerpendicularTo(Vec3d direction)
        {
            var up = new Vec3d(0.0, 1.0, 0.0);
            Vec3d side = Vec3d.Cross(direction, up);
            if (side.Magnitude < 1e-9)
            {
                side = Vec3d.Cross(direction, new Vec3d(1.0, 0.0, 0.0));
            }

            return side.Normalized;
        }

        /// <summary>
        /// LOD と実スケール引き渡しの確認 (Step 2 / 3b の視点を移設)。
        /// 旧 VerifyCapture の 01〜04 に対応する。
        /// </summary>
        static Scenario CreateLod(SolarSystemModel model, string name, double distance,
                                  bool fromEarth, string description,
                                  string[] checkPointsOverride = null)
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

            // **確認項目は 1〜3 行 (ScenarioTests が縛っている)。**
            // 足すのではなく差し替える。
            string[] checkPoints = checkPointsOverride ?? new[]
            {
                "火星が視界の中央にある",
                "光点とメッシュの切替が破綻していない",
            };

            return new Scenario(name, description, start, checkPoints);
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
