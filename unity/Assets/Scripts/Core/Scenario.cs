using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 検証シナリオの初期状態 (Step 8-0)。
    ///
    /// **姿勢は Quaternion ではなく「向く先」で持つ。** Core に Quaternion は無く、
    /// オイラー角は順序の解釈で揺れる。LookAt + Up なら Unity 側で
    /// Quaternion.LookRotation に素直に落ちるうえ、
    /// 「Position != LookAt」「Up が零でない」という破綻条件を
    /// EditMode テストで直接検証できる。
    /// </summary>
    public struct ScenarioStart
    {
        /// <summary>船の絶対位置。</summary>
        public Vec3d Position;

        /// <summary>機首を向ける絶対位置。姿勢はここから導く。</summary>
        public Vec3d LookAt;

        /// <summary>ロールの基準。既定は (0, 1, 0)。</summary>
        public Vec3d Up;

        /// <summary>目標ステーションの番号。</summary>
        public int TargetStationIndex;

        /// <summary>開始時刻 [秒]。UniverseClock に入れる。</summary>
        public double ElapsedSeconds;

        /// <summary>カメラの垂直画角 [度]。</summary>
        public double VerticalFovDegrees;

        /// <summary>起動直後にデバッグ HUD を出すか。</summary>
        public bool DebugHudVisible;

        /// <summary>
        /// 太陽方向の上書き (省略可)。
        ///
        /// 未設定なら SolarSystemModel が観測者位置から計算した向きを使う。
        /// 設定すると明暗境界線の角度を任意に決められる
        /// (Step 8 の earth-close で使う予定。8-0 では未設定)。
        /// **向きベクトル。正規化は使う側で行う。**
        /// </summary>
        public Vec3d? SunDirectionOverride;
    }

    /// <summary>検証シナリオ 1 件 (Step 8-0)。UnityEngine に依存しない。</summary>
    public sealed class Scenario
    {
        /// <summary>確認項目の最大行数。画面右上に収める。</summary>
        public const int MaxCheckPoints = 3;

        public Scenario(string name, string description, ScenarioStart start,
                        IReadOnlyList<string> checkPoints)
        {
            Name = name;
            Description = description;
            Start = start;
            CheckPoints = checkPoints;
        }

        /// <summary>起動引数 -scenario に渡す名前。</summary>
        public string Name { get; }

        /// <summary>1 行の説明。</summary>
        public string Description { get; }

        public ScenarioStart Start { get; }

        /// <summary>画面右上に出す確認項目 (1..MaxCheckPoints 行)。</summary>
        public IReadOnlyList<string> CheckPoints { get; }
    }
}
