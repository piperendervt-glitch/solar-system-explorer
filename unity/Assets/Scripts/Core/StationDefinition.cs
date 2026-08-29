using System;

namespace SolarSystem.Core
{
    /// <summary>航法灯 1 灯 (Step 13-1a)。**使っていない。空配列で確定** (Step 13 クローズ)。</summary>
    public readonly struct NavLight
    {
        public NavLight(Vec3d localPosition, Rgb color, double periodSeconds, double phase01)
        {
            LocalPosition = localPosition;
            Color = color;
            PeriodSeconds = periodSeconds;
            Phase01 = phase01;
        }

        /// <summary>プレハブ原点基準の位置。</summary>
        public Vec3d LocalPosition { get; }

        public Rgb Color { get; }

        /// <summary>点滅周期 [s]。実在の衝突防止灯に寄せて 1〜2 秒の想定。</summary>
        public double PeriodSeconds { get; }

        /// <summary>位相 0..1。**時刻から引く**ので、フレームレートに依らない。</summary>
        public double Phase01 { get; }
    }

    /// <summary>窓の発光 1 件 (Step 13-1a)。**使っていない。空配列で確定** (Step 13 クローズ)。</summary>
    public readonly struct WindowEmissive
    {
        public WindowEmissive(string materialName, double intensity)
        {
            MaterialName = materialName;
            Intensity = intensity;
        }

        /// <summary>棚卸し (13-2) で特定するマテリアル名。</summary>
        public string MaterialName { get; }

        /// <summary>発光の強さ。bloom のしきい値（決めた当時 0.90 / **13-3b で 3.00 へ変更**） との関係は 13-4 で F4 で決める。</summary>
        public double Intensity { get; }
    }

    /// <summary>
    /// **ステーションの定義 (Step 13-1a)。`CockpitDefinition` と対。**
    ///
    /// ■ 本丸は「ステーションごとの定数をグローバルから定義側へ移す」こと
    /// これまで `SpaceStation.PortStandoffKm`（= 半径 × 1.2）と
    /// `UniverseConstants.ArrivalRadiusUnits`（= 20）はグローバルだった。
    /// **モデルが変われば寸法も要求距離も変わる**ので、定義側に持つ。
    ///
    /// ■ **書き忘れが静かに成立しないようにしてある**
    /// 数値は `RequiredDouble`。`default` のまま読むと例外になる。
    /// 生成は `Create` だけ（コンストラクタは private）。
    ///
    /// ■ 地球と火星は**同じ定義を共有する**
    /// 位置と姿勢は配置側（`SpaceStation`）が持つ。差別化（灯の色・周期）は
    /// 13-4 の定数で行う予定だったが、**13-4 は実施しないので差別化は無い。**
    ///
    /// ■ `NavLights` / `WindowEmissives` は**空のままで確定**
    /// **空が意図。** 13-4（遠景と発光）で埋める予定だったが、
    /// **13-4 は実施しない**（人間が指示した項目ではないため / Step 13 クローズ）。
    /// 型と読む経路は残してある。将来使い始めたときに気づけるよう、
    /// **空であることを `StationDefinitionTests` が縛っている**（中身を入れると落ちる）。
    /// 落ちたら「使い始めた」ということなので、テストのほうを直す。
    /// </summary>
    public sealed class StationDefinition
    {
        /// <summary>箱ステーションの Id。**アセットが無いときのフォールバック。**</summary>
        public const string BoxId = "box";

        /// <summary>
        /// F4 の「ステーションの倍率」の既定値 (Step 13-3 コミット2)。
        /// **係数なので定義に依らず 1.0**（= 定義の `Scale` そのまま）。
        /// パネル側で数値を二重定義しないための出所。
        /// </summary>
        public const double RuntimeScaleFactorDefault = 1.0;

        readonly RequiredDouble _scale;
        readonly RequiredDouble _modelRadius;
        readonly RequiredDouble _hullAheadOfPort;
        readonly RequiredDouble _portStandoff;
        readonly RequiredDouble _requestRange;
        readonly NavLight[] _navLights;
        readonly WindowEmissive[] _windowEmissives;

        /// <summary>
        /// **F4 専用の実行時上書き (Step 13-3 コミット2)。** null なら `Scale` を使う。
        /// アセットにもコードの定数にも書き戻さない（F4 の運用 / §0-C）。
        /// </summary>
        double? _runtimeScale;

        /// <summary>**F4 専用の実行時上書き (Step 13-3b)。** null なら `RequestRange`。</summary>
        double? _runtimeRequestRange;

        StationDefinition(string id, string prefabGuid, RequiredDouble scale,
                          RequiredDouble modelRadius, RequiredDouble hullAheadOfPort,
                          Vec3d portLocal, Vec3d portForward, Vec3d portUp,
                          RequiredDouble portStandoff, RequiredDouble requestRange,
                          NavLight[] navLights, WindowEmissive[] windowEmissives,
                          string fallbackId)
        {
            Id = id;
            PrefabGuid = prefabGuid;
            _scale = scale;
            _modelRadius = modelRadius;
            _hullAheadOfPort = hullAheadOfPort;
            PortLocal = portLocal;
            PortForward = portForward;
            PortUp = portUp;
            _portStandoff = portStandoff;
            _requestRange = requestRange;
            _navLights = navLights ?? Array.Empty<NavLight>();
            _windowEmissives = windowEmissives ?? Array.Empty<WindowEmissive>();
            FallbackId = fallbackId;
        }

        /// <summary>
        /// 定義を作る。**すべての項目を明示すること**（省略可能な引数を作らない）。
        ///
        /// 数値は `RequiredDouble` なので、`default` を渡してもここは通り、
        /// **読んだ時点で例外になる。** 「書いたか」ではなく「読めるか」で塞ぐ。
        /// </summary>
        public static StationDefinition Create(
            string id, string prefabGuid, RequiredDouble scale, RequiredDouble modelRadius,
            RequiredDouble hullAheadOfPort,
            Vec3d portLocal, Vec3d portForward, Vec3d portUp,
            RequiredDouble portStandoff, RequiredDouble requestRange,
            NavLight[] navLights, WindowEmissive[] windowEmissives, string fallbackId)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id が空", nameof(id));
            }

            return new StationDefinition(id, prefabGuid, scale, modelRadius, hullAheadOfPort,
                                         portLocal, portForward, portUp,
                                         portStandoff, requestRange,
                                         navLights, windowEmissives, fallbackId);
        }

        /// <summary>定義の名前。**シーンに焼き込まれ、HUD とテストから読める。**</summary>
        public string Id { get; }

        /// <summary>プレハブの GUID。**箱は null / 空。**</summary>
        public string PrefabGuid { get; }

        /// <summary>プレハブを要求する定義か。箱は false。</summary>
        public bool NeedsPrefab => !string.IsNullOrEmpty(PrefabGuid);

        /// <summary>
        /// プレハブに掛ける倍率。**メートル単位のモデルは 0.001**
        /// （1 unit = 1 km。コックピットの 1000 倍空間とは逆方向）。
        /// 13-3 で実寸から補正を確定する。**未設定なら読んだ時点で例外。**
        /// </summary>
        public double Scale => _scale.Value;

        /// <summary>
        /// **実際に効く倍率 (Step 13-3 コミット2)。** 既定は `Scale`。
        /// F4 で振っている間だけ上書きが入る。**描画も判定もここを読む。**
        /// </summary>
        public double EffectiveScale => _runtimeScale ?? Scale;

        /// <summary>F4 が実行時に振るときだけ呼ぶ。**アセットには書き戻さない。**</summary>
        public void SetRuntimeScale(double value)
        {
            if (!(value > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "倍率は正");
            }

            _runtimeScale = value;
        }

        /// <summary>
        /// **太陽電池アレイのロール [度] (Step 13-3b)。**
        ///
        /// 姿勢は「ポート方向 = `PortDirection`」「`PortUp` = 太陽の方向」で決まるが、
        /// **明暗境界線ビューでの見栄えは目で決める値**なので、ポート方向のまわりに
        /// 振れるようにしてある。**既定は 0（アレイの法線がちょうど太陽を向く）。**
        /// **値は決めていない。** F4 で振って人間が決める。
        /// </summary>
        public double ArrayRollDegrees { get; private set; }

        /// <summary>F4 が実行時に振るときだけ呼ぶ。**アセットには書き戻さない。**</summary>
        public void SetArrayRoll(double degrees) => ArrayRollDegrees = degrees;

        /// <summary>F4 の R（既定へ戻す）で呼ぶ。</summary>
        public void ResetRuntimeScale() => _runtimeScale = null;

        /// <summary>
        /// **実際に効く要求可能距離 [units] (Step 13-3b)。** 既定は `RequestRange`。
        /// F4 で振っている間だけ上書きが入る。**判定も HUD もここを読む。**
        /// </summary>
        public double EffectiveRequestRange => _runtimeRequestRange ?? RequestRange;

        /// <summary>F4 が実行時に振るときだけ呼ぶ。**アセットには書き戻さない。**</summary>
        public void SetRuntimeRequestRange(double value)
        {
            if (!(value > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "要求可能距離は正");
            }

            _runtimeRequestRange = value;
        }

        /// <summary>F4 の R（既定へ戻す）で呼ぶ。</summary>
        public void ResetRuntimeRequestRange() => _runtimeRequestRange = null;

        /// <summary>F4 の R。アレイのロールも既定へ戻す。</summary>
        public void ResetArrayRoll() => ArrayRollDegrees = 0.0;

        /// <summary>F4 で振られているか。**HUD とテストが「振ったまま」を見分ける口。**</summary>
        public bool HasRuntimeScale => _runtimeScale.HasValue;

        /// <summary>
        /// **構造物の外接球の半径 [プレハブ単位]。** ピボット基準。
        /// 箱は 0.25、Cobble のモデルは 45.0777（13-3a の実測）。
        /// **units ではない。** units は `RadiusUnits`。
        /// </summary>
        public double ModelRadius => _modelRadius.Value;

        /// <summary>
        /// **構造物の外接球の半径 [units]。** `ModelRadius * EffectiveScale`。
        /// **定数として焼かない。** Scale を振ったら必ず追随する。
        /// </summary>
        public double RadiusUnits => ModelRadius * EffectiveScale;

        /// <summary>
        /// **ポートより前方へ構造物がはみ出している量 [プレハブ単位]。**
        ///
        /// 箱はポートが中心にあるので、半径ぶん（0.25）はみ出す。
        /// Cobble はポート面が構造全体の最前端なので **0**
        /// （13-3a の実測: z > 24.7182 に頂点を持つレンダラーは 0 件）。
        /// </summary>
        public double HullAheadOfPortLocal => _hullAheadOfPort.Value;

        /// <summary>同 [units]。`HullAheadOfPortLocal * EffectiveScale`。</summary>
        public double HullAheadOfPortUnits => HullAheadOfPortLocal * EffectiveScale;

        /// <summary>
        /// near clip を満たす `PortStandoff` の下限 [units]。
        ///
        /// ■ **式を差し替えた (Step 13-3b)**
        /// 旧: `RadiusUnits + nearClip`。**`PortLocal = 0`（ポートが構造物の中心）**
        /// を前提にした箱の式で、ポートが端にあるモデルでは過大になる
        /// （Scale 0.008 で 0.371 units = 371 m。直径 4.19 m の口の 371 m 手前）。
        ///
        /// 新: **`HullAheadOfPortUnits + nearClip`。**
        /// 制約は「目から最も近い構造物までが near clip 以上」。
        /// 目はポート面から `PortStandoff` だけ前方にいるので、
        /// 最近傍までの距離は `PortStandoff - HullAheadOfPortUnits`。
        /// これが `nearClip` 以上、を解いた形。
        ///
        /// **箱では値が変わらない**（`HullAheadOfPort` = 半径 0.25 なので 0.26 のまま）。
        /// </summary>
        public double MinStandoff(double nearClip) => HullAheadOfPortUnits + nearClip;

        /// <summary>ドッキング後の目から最も近い構造物までの距離 [units]。</summary>
        public double DockedClearance => PortStandoff - HullAheadOfPortUnits;

        /// <summary>ポート位置（プレハブ原点基準）。</summary>
        public Vec3d PortLocal { get; }

        /// <summary>
        /// **ポート位置 [units]。`PortLocal * EffectiveScale`。**
        /// 世界位置を別途持たない。ワールドへ写すのは `SpaceStation`（`StationBasis` 経由）。
        /// </summary>
        public Vec3d PortLocalUnits => PortLocal * EffectiveScale;

        /// <summary>
        /// ポート正面。**上方と 2 軸で持つ**（`EyeForward` / `EyeUp` と同じ理由。
        /// 1 軸だと回転軸が一意に決まらず、前後と上下が同時に反転しうる / Step 11-2c）。
        /// </summary>
        public Vec3d PortForward { get; }

        public Vec3d PortUp { get; }

        /// <summary>
        /// ポート面までの距離 [units]。**未設定なら読んだ時点で例外。**
        ///
        /// **下限がある。** ドッキング後のカメラ〜構造物の距離が
        /// Nearfield の near clip 0.01 units（10 m）を下回ってはいけない
        /// （下回ると構造物が near の内側に入って消える）。
        /// 数表は `StationDefinitionTests` にある。
        /// </summary>
        public double PortStandoff => _portStandoff.Value;

        /// <summary>
        /// ドッキング要求ができる距離 [units]。**未設定なら読んだ時点で例外。**
        /// 箱の既定は `UniverseConstants.ArrivalRadiusUnits`（= 20 / 決定 D-10）。
        /// </summary>
        public double RequestRange => _requestRange.Value;

        /// <summary>航法灯。**空で確定**（13-4 は実施しない）。テストが空を縛っている。</summary>
        public NavLight[] NavLights => _navLights;

        /// <summary>窓の発光。**空で確定**（13-4 は実施しない）。テストが空を縛っている。</summary>
        public WindowEmissive[] WindowEmissives => _windowEmissives;

        /// <summary>取り込まれていなかったときに落ちる先の Id。箱自身は null。</summary>
        public string FallbackId { get; }

        public override string ToString()
            => $"{Id} (scale={_scale} / modelRadius={_modelRadius}"
               + $" / hullAhead={_hullAheadOfPort}"
               + $" / standoff={_portStandoff} / range={_requestRange}"
               + $" / navLights={_navLights.Length} / emissives={_windowEmissives.Length})";
    }
}
