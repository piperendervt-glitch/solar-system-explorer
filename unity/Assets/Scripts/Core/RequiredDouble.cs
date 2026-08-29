using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// **書き忘れを静かに通さない double (Step 13-1a)。**
    ///
    /// ■ なぜ生の `double` ではいけないのか
    /// `Scale` の 1.0、`PortStandoff` の 0.3、`RequestRange` の 20 は、
    /// **どれも書き忘れても静かに成立してしまう値**（既定値が無害な値 /
    /// CLAUDE.md §0-B）。`double` の `default` は 0 で、これも
    /// 「止まっている」「原点にある」として通ってしまう場面がある。
    ///
    /// **`default` は「設定されていない」不正な状態**にして、読んだ時点で例外にする。
    /// 正常な値はファクトリ (`Positive`) でしか作れない。
    /// `ScenarioMotion` (Step 13-0b) と同じ形。
    ///
    /// ■ **読む経路が無ければ塞ぎにならない**
    /// `default` の例外は**読まれて初めて出る。** 「定義に書いた」だけでは
    /// 何も保証しない（CLAUDE.md §0-A「設定を書いたことと、経路が通っていることは別」）。
    /// 項目を足したら、読む側も同時に用意すること。
    /// </summary>
    public readonly struct RequiredDouble : IEquatable<RequiredDouble>
    {
        readonly bool _set;
        readonly double _value;

        RequiredDouble(double value)
        {
            _set = true;
            _value = value;
        }

        /// <summary>**正の数だけを受ける。** 0 や負は書き間違いとして弾く。</summary>
        public static RequiredDouble Positive(double value)
        {
            if (!(value > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "正の数であること");
            }

            if (double.IsInfinity(value) || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "有限の数であること");
            }

            return new RequiredDouble(value);
        }

        /// <summary>
        /// **0 以上を受ける (Step 13-3b)。**
        ///
        /// `Positive` が使えない項目のためにある。`HullAheadOfPort` は
        /// **0 が正しい値**（ポート面が構造全体の最前端で、前方へのはみ出しが無い）。
        /// **それでも `default` は弾く**ので、書き忘れは読んだ時点で例外になる。
        ///
        /// **安易にこちらへ逃げないこと。** 0 が意味を持たない項目は `Positive` を使う。
        /// </summary>
        public static RequiredDouble NonNegative(double value)
        {
            if (!(value >= 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "0 以上であること");
            }

            if (double.IsInfinity(value) || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "有限の数であること");
            }

            return new RequiredDouble(value);
        }

        /// <summary>設定されているか。**検査用。** 値を読むなら例外に任せてよい。</summary>
        public bool IsSet => _set;

        /// <summary>値。**未設定なら例外。**</summary>
        public double Value
        {
            get
            {
                if (!_set)
                {
                    throw new InvalidOperationException(
                        "RequiredDouble が設定されていない。"
                        + "**RequiredDouble.Positive(値) で明示すること。**"
                        + " default のまま通すと、書き忘れが 0 や既定値として静かに成立する");
                }

                return _value;
            }
        }

        public bool Equals(RequiredDouble other) => _set == other._set && _value.Equals(other._value);

        public override bool Equals(object obj) => obj is RequiredDouble other && Equals(other);

        public override int GetHashCode() => (_set ? 1 : 0) ^ _value.GetHashCode();

        public override string ToString()
            => _set ? _value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)
                    : "**未設定**";
    }
}
