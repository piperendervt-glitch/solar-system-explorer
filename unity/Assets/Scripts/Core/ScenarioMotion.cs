using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// **場面の動きの状態 (Step 13-0b)。**
    ///
    /// ■ なぜ `double Beta` を直接持たせないのか
    /// struct のフィールドは `default` が 0 になる。速度を書き忘れたシナリオが
    /// **静かに「静止」として成立してしまう。**（Demo 3 で踏んだ「既定値が
    /// 無害な値」と同じ型。CLAUDE.md §0-B）
    ///
    /// **`default` は「どちらも選んでいない」不正な状態**にして、読んだ時点で
    /// 例外にする。正常な値はファクトリ (`Static` / `Cruise`) でしか作れない。
    ///
    /// ■ 段番号ではなく β の生値で持つ
    /// `SpeedDial` の段番号を持たせると、**段の構成を変えたときに同じシナリオが
    /// 別の場面になる。** 段は再現の手段であって場面の定義ではない。
    ///
    /// ■ 測定窓はここに入れない
    /// 何フレーム測るか・dt を固定するかは**「測り方」であって「場面」ではない。**
    /// 計測側の引数のままにする。
    /// </summary>
    public readonly struct ScenarioMotion : IEquatable<ScenarioMotion>
    {
        /// <summary>**`Unset` が `default`。** 0 を有効な値に割り当てない。</summary>
        public enum Kind
        {
            /// <summary>**書き忘れ。** 読むと例外。</summary>
            Unset = 0,

            /// <summary>静止。**β = 0 を明示的に選んだ状態。**</summary>
            Static = 1,

            /// <summary>巡航。</summary>
            Cruise = 2,
        }

        readonly Kind _kind;
        readonly double _beta;

        ScenarioMotion(Kind kind, double beta)
        {
            _kind = kind;
            _beta = beta;
        }

        /// <summary>静止（β = 0 を明示的に選ぶ）。</summary>
        public static ScenarioMotion Static() => new ScenarioMotion(Kind.Static, 0.0);

        /// <summary>
        /// 巡航。**視線方向へ β の速さで進む。**
        /// β は 0 より大きく、`UniverseConstants.MaxCruiseBeta` 以下であること。
        /// </summary>
        public static ScenarioMotion Cruise(double beta)
        {
            if (!(beta > 0.0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(beta), beta, "巡航の β は 0 より大きいこと（静止なら Static を使う）");
            }

            if (beta > UniverseConstants.MaxCruiseBeta)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(beta), beta,
                    "β が上限 " + UniverseConstants.MaxCruiseBeta + " を超えている");
            }

            return new ScenarioMotion(Kind.Cruise, beta);
        }

        /// <summary>**設定されているか。** 検査用。値を読むなら例外に任せてよい。</summary>
        public bool IsSet => _kind != Kind.Unset;

        /// <summary>どちらを選んだか。**未設定なら例外。**</summary>
        public Kind Selected
        {
            get
            {
                RequireSet();
                return _kind;
            }
        }

        /// <summary>速さ [c]。静止なら 0。**未設定なら例外。**</summary>
        public double Beta
        {
            get
            {
                RequireSet();
                return _beta;
            }
        }

        /// <summary>速さ [km/s]。**未設定なら例外。**</summary>
        public double SpeedKmPerSec => UniverseConstants.BetaToKmPerSec(Beta);

        /// <summary>動いているか。**未設定なら例外。**</summary>
        public bool IsMoving => Selected == Kind.Cruise;

        void RequireSet()
        {
            if (_kind == Kind.Unset)
            {
                throw new InvalidOperationException(
                    "ScenarioMotion が設定されていない。"
                    + "**シナリオの Motion に ScenarioMotion.Static() か Cruise(β) を明示すること。**"
                    + " default のまま通すと、書き忘れが静止として静かに成立する");
            }
        }

        public bool Equals(ScenarioMotion other)
            => _kind == other._kind && _beta.Equals(other._beta);

        public override bool Equals(object obj) => obj is ScenarioMotion other && Equals(other);

        public override int GetHashCode() => ((int)_kind * 397) ^ _beta.GetHashCode();

        public override string ToString()
        {
            switch (_kind)
            {
                case Kind.Static: return "Static (β=0)";
                case Kind.Cruise: return "Cruise (β=" + _beta.ToString("G6") + ")";
                default: return "**Unset**";
            }
        }
    }
}
