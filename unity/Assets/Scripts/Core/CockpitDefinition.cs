using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// コックピットの定義 (Step 11-0c)。**UnityEngine 非依存。**
    ///
    /// ■ **いまはフォールバック判定に要る分しか無い。**
    /// `Scale` / `EyeLocal` / `EyeForward` / `Screens[]` / `Emissives[]` は
    /// 計画書 11-2a で作る。使う段になってから足す。
    /// **見えないコードを残さない**（Step 6 の EmissionIntensity = 4.0 と同じ轍を
    /// 踏まないため）。
    ///
    /// ■ **「実際に取り込まれているか」はここでは判定しない。**
    /// GUID からプレハブを引くのは `AssetDatabase`（Editor 専用）なので、
    /// 判定は Editor 側（`CockpitCatalog`）にある。ここが持つのは
    /// 「どの船か」という定義だけ。
    ///
    /// これは制約ではなく正しい形になっている。**このプロジェクトはシーンを
    /// Editor スクリプトで毎回まっさら生成するので、箱か実アセットかの分岐は
    /// シーン生成時に一度決まって焼き込まれる。** 実行時に GUID を解決する
    /// 必要が無い。
    /// </summary>
    public sealed class CockpitDefinition
    {
        /// <summary>箱コックピットの Id。**アセットが無いときのフォールバック。**</summary>
        public const string BoxId = "box";

        /// <summary>Hi-Rez の無料サンプル (Step 11-1 で取り込む)。</summary>
        public const string HiRezSampleId = "hirez-sample";

        public CockpitDefinition(string id, string prefabGuid, string fallbackId,
                                 double scale = 1.0,
                                 Vec3d? eyeLocal = null,
                                 Vec3d? eyeForward = null,
                                 Vec3d? eyeUp = null)
        {
            Id = id;
            PrefabGuid = prefabGuid;
            FallbackId = fallbackId;
            Scale = scale;
            EyeLocal = eyeLocal;
            EyeForward = eyeForward ?? new Vec3d(0.0, 0.0, 1.0);
            EyeUp = eyeUp ?? new Vec3d(0.0, 1.0, 0.0);
        }

        /// <summary>定義の名前。**シーンに焼き込まれ、HUD とテストから読める。**</summary>
        public string Id { get; }

        /// <summary>
        /// プレハブの GUID。**箱は null / 空。**
        /// 実在するかは Editor 側が確かめる（ここでは分からない）。
        /// </summary>
        public string PrefabGuid { get; }

        /// <summary>取り込まれていなかったときに落ちる先の Id。箱自身は null。</summary>
        public string FallbackId { get; }

        /// <summary>プレハブを要求する定義か。箱は false。</summary>
        public bool NeedsPrefab => !string.IsNullOrEmpty(PrefabGuid);

        /// <summary>
        /// プレハブに掛ける倍率。**メートル単位のアセットは 1.0 のまま。**
        ///
        /// コックピットは 1000 倍の描画空間にあり、そこでは **1 m = 1 unit**
        /// （`CameraStackController.CockpitRenderScale`）。実寸で作られたアセットは
        /// 倍率を掛けずに置ける。ずれていたらここで吸収する (11-2c)。
        /// </summary>
        public double Scale { get; }

        /// <summary>
        /// 座席の目の位置（プレハブ原点基準・メートル）。
        ///
        /// **null は「プレハブの bounds から出す」の意味。**
        /// 目の位置は絵を見て決める値なので、決まるまで嘘の定数を置かない。
        /// 初期値の出し方と実測値は `CockpitBoundsSolver` がログに出す (11-2b)。
        /// F4 で決まったらここに定数として入れる。
        /// </summary>
        public Vec3d? EyeLocal { get; }

        /// <summary>
        /// **このアセットの機首がプレハブ空間のどちらを向いているか。** 既定は Z+。
        ///
        /// 船の前方は Unity の Z+ なので、ここが Z+ でないアセットは
        /// `FromToRotation(EyeForward, Z+)` で**プレハブ側を回して合わせる。**
        /// カメラを回すのではない（カメラを回すと船の後ろを向いてしまう）。
        ///
        /// **値は決め打ちではなく実測。** Hi-Rez は Z+（11-2c）。
        /// 座席 (0, -0.074, -1.436) から見て、操縦桿 (0, 0.060, -0.887) と
        /// 計器の画面 (±0.205, 0.148, -0.422) が **+Z 側に並ぶ。**
        /// **計器と操縦桿は操縦者の前にあるので、そちらが前。**
        ///
        /// **一度 -Z と書いたが誤り。** 窓の中心 z = -1.515 が負であることだけを見て
        /// 決めていた。窓は座席の上を覆っているので前後の判断材料にならない
        /// （窓は z = -2.81 〜 -0.22 に広がり、座席・操縦桿・画面をすべて覆っている）。
        /// </summary>
        public Vec3d EyeForward { get; }

        /// <summary>
        /// **このアセットの上方向がプレハブ空間のどちらを向いているか。** 既定は Y+。
        ///
        /// ■ なぜ前方だけでは足りないか（実機で踏んだ）
        /// 前方だけから `FromToRotation(EyeForward, Z+)` で回転を作ると、
        /// **EyeForward が Z+ と反平行のとき回転軸が一意に決まらない。**
        /// Unity は直交する軸を任意に 1 本選んで 180 度回すので、X 軸が選ばれると
        /// **前後と上下が同時に反転する。** 実機ではまさにそう出た。
        ///
        /// 前方と上方の 2 軸で姿勢を決めれば（`LookRotation`）、反平行でも縮退しない。
        /// </summary>
        public Vec3d EyeUp { get; }

        /// <summary>
        /// **画角はここに持たない。**
        ///
        /// FOV は 4 段のカメラで共有する値（`CameraStackController.VerticalFovDegrees`、
        /// シナリオが上書きする）。コックピットだけ別の画角にすると、**窓枠と
        /// 窓の外の景色で遠近が食い違う。** F4 の FOV 項目もスタック全体に効かせる。
        /// </summary>

        /// <summary>箱コックピット。**取り込みが無くてもこれで組める。**</summary>
        public static CockpitDefinition Box { get; } =
            new CockpitDefinition(BoxId, prefabGuid: null, fallbackId: null);

        /// <summary>
        /// Hi-Rez の無料サンプル（内装付きコックピット）。
        ///
        /// **GUID は .unitypackage のフォルダ名から読んだ実測値。**
        /// .unitypackage は各エントリのフォルダ名が GUID そのものなので、
        /// 取り込む前に確定する。取り込み後に解決できることは
        /// EditMode テストが縛る（定数と実態の一致）。
        ///
        /// 取り込まれていなければ箱へ落ちる（CockpitCatalog.Resolve）。
        /// </summary>
        public static CockpitDefinition HiRezSample { get; } = new CockpitDefinition(
            HiRezSampleId, "54e1b562c3fea284f8a0ec8cdc70057c", BoxId,
            // **メートル単位のアセットなので倍率は 1.0。** 実寸の確認は 11-2c。
            scale: 1.0,
            // **実機で確定 (11-2b)。**
            // 座席の水平位置 (0, -0.074, -1.436) に窓の中心の高さ (y = 0.429) を
            // 合わせた初期値を exe の F4 で振って確かめ、**動かす理由が無かったので
            // そのまま採用**した（F4 の表示は F2 書式で 0.43 / -1.44）。
            // ここに定数として置くことで、初期値の出し方（CockpitBoundsSolver）を
            // 将来変えても、このコックピットの目の位置は動かない。
            eyeLocal: new Vec3d(0.0, 0.429, -1.436),
            // **機首は -Z（実測。11-2c）。** 窓 Cockpit3_Glass の中心が z = -1.515 m で、
            // bounds の z は ±2.883。窓のあるほうが前。
            // **前は Z+（実測。11-2c）。** 座席 -> 操縦桿 -> 計器の画面が +Z 側に並ぶ。
            eyeForward: new Vec3d(0.0, 0.0, 1.0),
            // **上は Y+（実測。11-2c）。** 窓が座席の 0.50 m 上にあり
            // (窓 y=+0.429 / 座席 y=-0.074)、操縦桿も台 (y=-0.023) より
            // 握り (y=+0.060) のほうが上。座席の射出ハンドル (y=-0.499) が最下部。
            eyeUp: new Vec3d(0.0, 1.0, 0.0));

        /// <summary>
        /// 画面の発光強度の既定値 (Step 11-3b)。**目で決める値。**
        /// bloom のしきい値 0.90 の下に置き、文字が滲まないことを優先する。
        /// F4 で振って決めたらここを書き換える。
        /// </summary>
        public const double DefaultScreenEmission = 0.75;

        /// <summary>
        /// この定義の画面の割り当て (Step 11-3a / 11-3c で案 A に確定)。
        ///
        /// **箱は空。** 画面が空かどうかで、下端の帯を出すかが決まる。
        /// </summary>
        public IReadOnlyList<CockpitScreen> Screens =>
            Id == HiRezSampleId ? ScreensA : System.Array.Empty<CockpitScreen>();

        // 投影サイズの実測 (基準 1920x1080 / 画角 60 度 / 目 (0, 0.43, -1.44)):
        //   Screen-1  351x119 / Screen-2  351x119 / HUD-2 188x188
        //   Gauge2-Screen 178x139 / Gauge1-Screen 98x90
        // 面の実寸 (11-3b の UV 計測):
        //   Screen-1/-2 345.4 x 183.5 mm / HUD-2 189.2 x 189.2 / Gauge 76.5 x 76.5
        // **長辺だけを決め、短辺は実寸の比から CockpitBuilder が出す。**

        /// <summary>大画面に文字情報、HUD は最小限。**実機で見比べて確定 (11-3c)。**</summary>
        static readonly CockpitScreen[] ScreensA =
        {
            new CockpitScreen("CockpitEquipments_Screen-2", ScreenRole.Flight, 1024),
            new CockpitScreen("CockpitEquipments_Screen-1", ScreenRole.TargetFull, 1024),
            new CockpitScreen("CockpitEquipments_HUD-2", ScreenRole.Alignment, 512),
            new CockpitScreen("CockpitEquipments_Gauge2-Screen", ScreenRole.SpeedDial, 512),
            new CockpitScreen("CockpitEquipments_Gauge1-Screen", ScreenRole.Autopilot, 256),
        };

        public override string ToString() => Id;
    }
}
