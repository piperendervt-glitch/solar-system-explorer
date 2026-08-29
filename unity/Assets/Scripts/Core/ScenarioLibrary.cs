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

            // ゴースト (Step 9-3b) は太陽から視線をずらさないと出ない。
            // sun-face では中心に重なって見えない。
            list.Add(CreateSunOffAxis(model));

            // コックピットの配置 (Step 11-2b)。窓の面積比と、窓の外に地球が
            // 見えていることを見る。位置と光は earth-close-day と同じ。
            list.Add(CreateCockpitView(model));

            // **Nearfield 段の立体視を見る場面 (Step 12-2b)。**
            // 配布 (`XrEyeRig`) が効いたことを絵で示せるのはこの段だけ。
            // Deep と Near は距離が大きすぎて、配布の有無で何も変わらない。
            list.Add(CreateStationClose(model));

            // **巡航中の候補 (Step 13-0b)。人間が 1 件を選ぶまでの仮置き。**
            // 選ばれなかったものは削除する（「選ばれなかったものを残さない」）。
            foreach (Scenario cruise in CreateCruiseCandidates(model))
            {
                list.Add(cruise);
            }

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

        /// <summary>太陽を画面の端寄りに置く (Step 9-3b)。ゴーストの確認用。</summary>
        public const string SunOffAxisName = "sun-offaxis";

        /// <summary>
        /// コックピット越しに地球を見る (Step 11-2b)。
        /// **窓の投影面積比を測るための場面。** 地球が窓の中にあることを目でも
        /// 画素でも確かめられるよう、`earth-close-day` と同じ位置・同じ光にしてある。
        /// </summary>
        public const string CockpitViewName = "cockpit-view";

        /// <summary>
        /// **ステーションのすぐ手前 (Step 12-2b)。**
        /// 頭姿勢の段配布が Nearfield 段の絵に届いたかを見るための場面。
        /// </summary>
        public const string StationCloseName = "station-close";

        /// <summary>
        /// ステーションの中心からの距離 [units]。
        ///
        /// **近いほど視差が大きく出るが、半径 0.25 units の中には入れない。**
        /// 0.5 units なら角直径 2*atan(0.25/0.5) = 0.927 rad = 53 度で、
        /// 画角 (XR で 111 度) に収まったまま画面の半分ほどを占める。
        /// ドッキング口の標準停止位置 (PortStandoff = 0.3 units) の少し外。
        /// </summary>
        public const double StationCloseDistanceUnits = 0.5;

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
        /// コックピット越しに地球を見る (Step 11-2b)。
        ///
        /// **`earth-close-day` と同じ位置・同じ光**にしてある。違うのは確認項目だけで、
        /// 見るのは惑星ではなくコックピットの側。同じ絵で比べられるようにしている。
        /// </summary>
        static Scenario CreateCockpitView(SolarSystemModel model)
        {
            var offset = new Vec3d(1.0, 0.0, 0.0);
            Vec3d position = model.Earth.AbsolutePosition
                             + offset * (SolarSystemModel.EarthRadiusKm * EarthCloseRadii);

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = model.Earth.AbsolutePosition,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                Motion = ScenarioMotion.Static(),
                DebugHudVisible = false,
                SunDirectionOverride = offset * -1.0, // 昼側。earth-close-day と同じ
            };

            return new Scenario(CockpitViewName, "コックピット越しに地球を見る", start, new[]
            {
                "窓の外に地球が見えている (窓枠に隠れていない)",
                "正面窓の上端が画面の上端より上、計器盤の上端が下 1/3 より下",
                "ガラス越しの景色が暗くなりすぎていない",
            });
        }

        // ---- 巡航中の候補 (Step 13-0b) ----
        //
        // **β は最上段の 0.9**（`DefaultCruiseBeta`）。1 km/s では 1 フレームで
        // 0.0167 units しか進まず、実質もう 1 枚の静止画になる。
        // 0.9c は dt=1/60 で **1 フレーム 4,496.887 units**。
        //
        // **どれを使うかは人間が選ぶ。** 選ばれなかった候補は削除する。

        public const string CruiseAName = "cruise-a";
        public const string CruiseBName = "cruise-b";
        public const string CruiseCName = "cruise-c";
        public const string CruiseDName = "cruise-d";
        public const string CruiseEName = "cruise-e";

        static List<Scenario> CreateCruiseCandidates(SolarSystemModel model)
        {
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d mars = model.Mars.AbsolutePosition;
            Vec3d towardMars = (mars - earth).Normalized;
            SpaceStation earthStation = model.Stations[0];
            SpaceStation marsStation = model.Stations[1];

            return new List<Scenario>
            {
                // 出港直後。ステーションが背後へ抜け、地球が引き渡し帯を通る。
                Cruise(CruiseAName, "出港直後（地球Stのポート -> 火星St）",
                       earthStation.PortPosition, marsStation.AbsolutePosition, 1),

                // 深宇宙。前後に何も無い（地球も火星も 0.3 px 未満）。
                Cruise(CruiseBName, "深宇宙の巡航（中間点 -> 火星）",
                       earth + (towardMars * (SolarSystemModel.EarthToMarsKm * 0.5)), mars, 1),

                // 火星の引き渡し帯に入った状態から。開始時に火星が 126.8 px。
                Cruise(CruiseCName, "火星の引き渡し帯から（5e4 -> 通過）",
                       mars - (towardMars * 5.0e4), mars, 1),

                // 地球を離れる。前方は星空だけ。
                Cruise(CruiseDName, "地球を離れる（前方は星空）",
                       earth + new Vec3d(SolarSystemModel.EarthRadiusKm * EarthCloseRadii, 0.0, 0.0),
                       earth + new Vec3d(SolarSystemModel.EarthToMarsKm, 0.0, 0.0), 0),

                // 地球へ接近して通過する。引き渡し帯を frame 0〜22 で跨ぐ。
                Cruise(CruiseEName, "地球へ接近して通過（5e4 -> 通過）",
                       earth + (towardMars * 5.0e4), earth, 0),
            };
        }

        /// <summary>
        /// 巡航の候補を 1 件作る。**β は `DefaultCruiseBeta` で固定。**
        /// 開始点と視線だけが候補ごとに違う。
        /// </summary>
        static Scenario Cruise(string name, string description,
                               Vec3d position, Vec3d lookAt, int targetStationIndex)
        {
            var start = new ScenarioStart
            {
                Position = position,
                LookAt = lookAt,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = targetStationIndex,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                Motion = ScenarioMotion.Cruise(UniverseConstants.DefaultCruiseBeta),
                DebugHudVisible = false,
                SunDirectionOverride = null,
            };

            return new Scenario(name, description, start, new[]
            {
                "船が視線方向へ進んでいる（HUD の速度が 0 でない）",
                "フレーム時間の計測に使う場面であって、絵の良し悪しは見ない",
            });
        }

        /// <summary>
        /// ステーションのすぐ手前に立つ (Step 12-2b)。
        ///
        /// **視差の理論値（12-1 の f * 眼間 * s / D）が桁で効く唯一の場面。**
        /// D = 0.5 units なので、配布前（眼間がメートルのまま）と配布後で
        /// 左右のずれがちょうど 1000 倍違う。
        /// </summary>
        static Scenario CreateStationClose(SolarSystemModel model)
        {
            SpaceStation station = model.Stations[0];

            // ドッキング口の側から見る。**station の中心を見る**ので、
            // 画面中央に既知の距離の物がある形になる。
            Vec3d position = station.AbsolutePosition
                             + station.PortDirection * StationCloseDistanceUnits;

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = station.AbsolutePosition,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                Motion = ScenarioMotion.Static(),
                DebugHudVisible = false,

                // ステーションを正面から照らす（陰にして測れなくならないように）。
                SunDirectionOverride = station.PortDirection * -1.0,
            };

            return new Scenario(StationCloseName,
                                "ステーションのすぐ手前 (中心まで 0.5 units)", start, new[]
            {
                "窓の外にステーションが見えていて、画面からはみ出していない",
                "XR: 左右の目でステーションがほぼ同じ位置にある (融合できる)",
            });
        }

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
                Motion = ScenarioMotion.Static(),
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
                Motion = ScenarioMotion.Static(),
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
                Motion = ScenarioMotion.Static(),
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
                Motion = ScenarioMotion.Static(),
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
                Motion = ScenarioMotion.Static(),
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

        /// <summary>
        /// 太陽を画面の端寄りに見る (Step 9-3b)。**ゴーストの確認用。**
        ///
        /// 位置は sun-face と同じ。**視線だけを太陽からずらす。**
        /// 縦 FOV 60 度の 1/3 = 10 度ぶん傾けると、太陽は画面中心から
        /// 縦方向に H/3 ほど外れた位置に来る。ゴーストはその反対側へ並ぶ。
        /// </summary>
        static Scenario CreateSunOffAxis(SolarSystemModel model)
        {
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d sun = model.Sun.AbsolutePosition;
            Vec3d towardSun = (sun - earth).Normalized;
            Vec3d position = earth + towardSun * 1.0e5;

            // 視線を太陽から 10 度ずらす。LookAt は「太陽の隣」を指す点。
            double offsetDegrees = UniverseConstants.ReferenceVerticalFovDegrees / 3.0;
            double distance = Vec3d.Distance(sun, position);
            Vec3d side = PerpendicularTo(towardSun);
            Vec3d lookAt = sun + side * (distance * System.Math.Tan(
                offsetDegrees * System.Math.PI / 180.0));

            var start = new ScenarioStart
            {
                Position = position,
                LookAt = lookAt,
                Up = new Vec3d(0.0, 1.0, 0.0),
                TargetStationIndex = 0,
                ElapsedSeconds = 0.0,
                VerticalFovDegrees = UniverseConstants.ReferenceVerticalFovDegrees,
                Motion = ScenarioMotion.Static(),
                DebugHudVisible = false,
                SunDirectionOverride = null,
            };

            return new Scenario(SunOffAxisName, "太陽を画面の端寄りに見る (ゴーストの確認)", start, new[]
            {
                "太陽が画面の中心から外れた位置にある",
                "太陽の反対側に小さな円が並ぶ (ゴースト)",
                "太陽を画面外へ出すとゴーストも消える",
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
                Motion = ScenarioMotion.Static(),
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
                Motion = ScenarioMotion.Static(),
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
