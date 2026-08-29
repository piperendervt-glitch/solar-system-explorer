using System;

namespace SolarSystem.Core
{
    /// <summary>航法灯 1 灯 (Step 13-1a)。**13-4 で使う。今は空配列。**</summary>
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

    /// <summary>窓の発光 1 件 (Step 13-1a)。**13-4 で使う。今は空配列。**</summary>
    public readonly struct WindowEmissive
    {
        public WindowEmissive(string materialName, double intensity)
        {
            MaterialName = materialName;
            Intensity = intensity;
        }

        /// <summary>棚卸し (13-2) で特定するマテリアル名。</summary>
        public string MaterialName { get; }

        /// <summary>発光の強さ。bloom しきい値 0.90 との関係は 13-4 で F4 で決める。</summary>
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
    /// 13-4 の定数で行うので、この回では入れない。
    ///
    /// ■ `NavLights` / `WindowEmissives` は**まだ使っていない**
    /// 型と読む経路だけ用意してある（13-4 で使う）。空配列が既定。
    /// </summary>
    public sealed class StationDefinition
    {
        /// <summary>箱ステーションの Id。**アセットが無いときのフォールバック。**</summary>
        public const string BoxId = "box";

        readonly RequiredDouble _scale;
        readonly RequiredDouble _portStandoff;
        readonly RequiredDouble _requestRange;
        readonly NavLight[] _navLights;
        readonly WindowEmissive[] _windowEmissives;

        StationDefinition(string id, string prefabGuid, RequiredDouble scale,
                          Vec3d portLocal, Vec3d portForward, Vec3d portUp,
                          RequiredDouble portStandoff, RequiredDouble requestRange,
                          NavLight[] navLights, WindowEmissive[] windowEmissives,
                          string fallbackId)
        {
            Id = id;
            PrefabGuid = prefabGuid;
            _scale = scale;
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
            string id, string prefabGuid, RequiredDouble scale,
            Vec3d portLocal, Vec3d portForward, Vec3d portUp,
            RequiredDouble portStandoff, RequiredDouble requestRange,
            NavLight[] navLights, WindowEmissive[] windowEmissives, string fallbackId)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id が空", nameof(id));
            }

            return new StationDefinition(id, prefabGuid, scale, portLocal, portForward, portUp,
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

        /// <summary>ポート位置（プレハブ原点基準）。</summary>
        public Vec3d PortLocal { get; }

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

        /// <summary>航法灯。**13-4 で使う。今は空。**</summary>
        public NavLight[] NavLights => _navLights;

        /// <summary>窓の発光。**13-4 で使う。今は空。**</summary>
        public WindowEmissive[] WindowEmissives => _windowEmissives;

        /// <summary>取り込まれていなかったときに落ちる先の Id。箱自身は null。</summary>
        public string FallbackId { get; }

        public override string ToString()
            => $"{Id} (scale={_scale} / standoff={_portStandoff} / range={_requestRange}"
               + $" / navLights={_navLights.Length} / emissives={_windowEmissives.Length})";
    }
}
