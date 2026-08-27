using System.Collections.Generic;
using System.Text;

namespace SolarSystem.Core
{
    public enum DebugItemKind
    {
        Toggle,
        Number,
        Choice,
    }

    /// <summary>デバッグパネルの 1 項目 (Step 8-0b)。UnityEngine 非依存。</summary>
    public sealed class DebugItem
    {
        public static DebugItem MakeToggle(string id, string label, bool defaultValue)
            => new DebugItem
            {
                Id = id,
                Label = label,
                Kind = DebugItemKind.Toggle,
                BoolValue = defaultValue,
                DefaultBool = defaultValue,
            };

        public static DebugItem MakeNumber(string id, string label, double defaultValue,
                                           double min, double max, double step, string format)
            => new DebugItem
            {
                Id = id,
                Label = label,
                Kind = DebugItemKind.Number,
                Value = defaultValue,
                DefaultValue = defaultValue,
                Min = min,
                Max = max,
                Step = step,
                Format = format,
            };

        public static DebugItem MakeChoice(string id, string label, string[] options, int defaultIndex)
            => new DebugItem
            {
                Id = id,
                Label = label,
                Kind = DebugItemKind.Choice,
                Options = options,
                Index = defaultIndex,
                DefaultIndex = defaultIndex,
            };

        public string Id { get; private set; }
        public string Label { get; private set; }
        public DebugItemKind Kind { get; private set; }

        public bool BoolValue { get; set; }
        public bool DefaultBool { get; private set; }

        public double Value { get; set; }
        public double DefaultValue { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Step { get; private set; }
        public string Format { get; private set; }

        public int Index { get; set; }
        public int DefaultIndex { get; private set; }
        public IReadOnlyList<string> Options { get; private set; }

        public bool IsChanged
        {
            get
            {
                switch (Kind)
                {
                    case DebugItemKind.Toggle:
                        return BoolValue != DefaultBool;
                    case DebugItemKind.Choice:
                        return Index != DefaultIndex;
                    default:
                        return System.Math.Abs(Value - DefaultValue) > Step * 0.001;
                }
            }
        }

        /// <summary>画面に出す現在値。数値は既定との差が分かる形。</summary>
        public string ValueText()
        {
            switch (Kind)
            {
                case DebugItemKind.Toggle:
                    return BoolValue ? "ON " : "off";

                case DebugItemKind.Choice:
                    return Options != null && Index >= 0 && Index < Options.Count
                        ? Options[Index]
                        : "?";

                default:
                    string now = Value.ToString(Format);
                    if (!IsChanged)
                    {
                        return now + "  (既定)";
                    }

                    double delta = Value - DefaultValue;
                    string sign = delta >= 0 ? "+" : "-";
                    return now + "  (既定 " + DefaultValue.ToString(Format) + " から "
                           + sign + System.Math.Abs(delta).ToString(Format) + ")";
            }
        }

        /// <summary>ログ用の 1 行。</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case DebugItemKind.Toggle:
                    return Label + ": " + (DefaultBool ? "ON" : "off") + " -> " + (BoolValue ? "ON" : "off");

                case DebugItemKind.Choice:
                    string was = Options[DefaultIndex];
                    return Label + ": " + was + " -> " + Options[Index];

                default:
                    return Label + ": " + DefaultValue.ToString(Format) + " -> " + Value.ToString(Format);
            }
        }

        public void Adjust(int delta)
        {
            switch (Kind)
            {
                case DebugItemKind.Toggle:
                    if (delta != 0)
                    {
                        BoolValue = !BoolValue;
                    }

                    break;

                case DebugItemKind.Choice:
                    if (Options == null || Options.Count == 0)
                    {
                        break;
                    }

                    Index = ((Index + delta) % Options.Count + Options.Count) % Options.Count;
                    break;

                default:
                    double next = Value + Step * delta;

                    // 刻みに乗せてからクランプする。押し続けても端数が残らない。
                    next = System.Math.Round(next / Step) * Step;
                    Value = System.Math.Min(Max, System.Math.Max(Min, next));
                    break;
            }
        }

        public void Reset()
        {
            BoolValue = DefaultBool;
            Value = DefaultValue;
            Index = DefaultIndex;
        }
    }

    /// <summary>
    /// デバッグパネルの状態 (Step 8-0b)。**UnityEngine 非依存。**
    ///
    /// 「目で決めるしかない値」を実機で決めるための操作盤。
    /// 実装依頼 -> 再ビルド -> 起動 -> 目視 の往復を無くすのが目的。
    ///
    /// **既定値は呼び手がコードの定数から渡す。** ここで数値を二重定義しない。
    /// </summary>
    public sealed class DebugPanelModel
    {
        /// <summary>「1 段だけ表示」の選択肢。0 = なし。</summary>
        public static readonly string[] SoloOptions =
            { "なし", "Deep", "Near", "Nearfield", "Cockpit" };

        public const string SoloId = "tier.solo";
        public const string AtmosphereId = "num.atmosphere";
        public const string CloudId = "num.cloud";
        public const string FlareId = "num.flare";
        public const string ShakeId = "num.shake";

        /// <summary>
        /// 太陽の HDR 発光強度 (Step 9-1)。**1 項目で光点と殻の両方に効く。**
        /// 別々に振ると LOD 帯で不連続になるので分けない。
        /// </summary>
        public const string SunEmissionId = "num.sunEmission";

        /// <summary>コロナの半径倍率 (Step 9-2)。太陽本体の何倍か。</summary>
        public const string CoronaSizeId = "num.coronaSize";

        /// <summary>光条の長さ (本体比) (Step 9-3b)。</summary>
        public const string SpikeLengthId = "num.spikeLength";

        /// <summary>
        /// **画面上の**光条の本数 (Step 9-3b)。要素数はこの半分。
        /// 目で数える値なので要素数ではなくこちらを項目の意味にした。
        /// </summary>
        public const string SpikeCountId = "num.spikeCount";

        /// <summary>ゴーストの強さ (Step 9-3b)。</summary>
        public const string GhostId = "num.ghost";

        /// <summary>Bloom の強度 (Step 9-4)。</summary>
        public const string BloomIntensityId = "num.bloomIntensity";

        /// <summary>Bloom のしきい値 (Step 9-4)。</summary>
        public const string BloomThresholdId = "num.bloomThreshold";

        /// <summary>Bloom の拡散 (Step 9-4)。</summary>
        public const string BloomScatterId = "num.bloomScatter";

        /// <summary>コロナの減衰の指数 (Step 9-4)。小さいほどハローが広がる。</summary>
        public const string CoronaFalloffId = "num.coronaFalloff";

        /// <summary>光条の太さ (Step 9-4)。</summary>
        public const string SpikeThicknessId = "num.spikeThickness";

        /// <summary>音量 (Step 10-2)。**耳で決める値なので実機で振る。**</summary>
        public const string MasterVolumeId = "num.volMaster";
        public const string EngineVolumeId = "num.volEngine";
        public const string CockpitVolumeId = "num.volCockpit";
        public const string SfxVolumeId = "num.volSfx";

        readonly List<DebugItem> _items = new List<DebugItem>();

        public IReadOnlyList<DebugItem> Items => _items;

        /// <summary>開いているか。閉じても設定は保持する。</summary>
        public bool IsOpen { get; private set; }

        public int Cursor { get; private set; }

        public DebugItem Current => _items.Count > 0 ? _items[Cursor] : null;

        /// <summary>
        /// 既定値はすべて呼び手が渡す。Core から Unity 側の定数は見えないので、
        /// Unity 側が SunFlareController.BaseIntensity などを持ってくる。
        /// </summary>
        public static DebugPanelModel Create(
            IReadOnlyList<string> bodyNames,
            double atmosphereStrength,
            double cloudOpacity,
            double flareIntensity,
            double shakeAmplitude,
            double sunEmission,
            double coronaSize,
            double spikeLength,
            double spikeCount,
            double ghostIntensity,
            double bloomIntensity,
            double bloomThreshold,
            double bloomScatter,
            double coronaFalloff,
            double spikeThickness,
            double masterVolume,
            double engineVolume,
            double cockpitVolume,
            double sfxVolume)
        {
            var m = new DebugPanelModel();

            m._items.Add(DebugItem.MakeChoice(SoloId, "1 段だけ表示", SoloOptions, 0));
            m._items.Add(DebugItem.MakeToggle("tier.deep", "カメラ段 Deep", true));
            m._items.Add(DebugItem.MakeToggle("tier.near", "カメラ段 Near", true));
            m._items.Add(DebugItem.MakeToggle("tier.nearfield", "カメラ段 Nearfield", true));
            m._items.Add(DebugItem.MakeToggle("tier.cockpit", "カメラ段 Cockpit", true));

            if (bodyNames != null)
            {
                foreach (string name in bodyNames)
                {
                    m._items.Add(DebugItem.MakeToggle(BodyId(name, "point"), name + " 光点", true));
                    m._items.Add(DebugItem.MakeToggle(BodyId(name, "proxy"), name + " プロキシ殻", true));
                    m._items.Add(DebugItem.MakeToggle(BodyId(name, "real"), name + " 実スケール", true));
                }
            }

            m._items.Add(DebugItem.MakeToggle("show.clouds", "雲層", true));
            m._items.Add(DebugItem.MakeToggle("show.stations", "ステーション", true));
            m._items.Add(DebugItem.MakeToggle("show.skybox", "スカイボックス", true));
            m._items.Add(DebugItem.MakeToggle("show.post", "ポストプロセス", true));
            m._items.Add(DebugItem.MakeToggle("show.flare", "レンズフレア", true));
            m._items.Add(DebugItem.MakeToggle("show.corona", "コロナ", true));

            m._items.Add(DebugItem.MakeNumber(AtmosphereId, "_AtmosphereStrength",
                atmosphereStrength, 0.0, 10.0, 0.25, "F2"));
            m._items.Add(DebugItem.MakeNumber(CloudId, "_CloudOpacity",
                cloudOpacity, 0.0, 2.0, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(FlareId, "フレア基準強度",
                flareIntensity, 0.0, 2.0, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(SunEmissionId, "太陽の発光強度",
                sunEmission, 0.0, 16.0, 0.25, "F2"));
            // **上限は 12.0。** 6.00 で確定したが、それは当時の上限 6.0 に
            // 張り付いた値だった。bloom 強度と同じ理由で広げてある。
            m._items.Add(DebugItem.MakeNumber(CoronaSizeId, "コロナの大きさ (本体比)",
                coronaSize, 1.0, 12.0, 0.25, "F2"));
            m._items.Add(DebugItem.MakeNumber(SpikeLengthId, "光条の長さ (本体比)",
                spikeLength, 0.0, 6.0, 0.25, "F2"));

            // **画面上の本数。** 要素 1 個で 2 本見えるので刻みは 2 (偶数のみ)。
            m._items.Add(DebugItem.MakeNumber(SpikeCountId, "光条の本数 (画面上)",
                spikeCount, 0.0, 12.0, 2.0, "F0"));
            m._items.Add(DebugItem.MakeNumber(GhostId, "ゴーストの強さ",
                ghostIntensity, 0.0, 2.0, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(SpikeThicknessId, "光条の太さ",
                spikeThickness, 0.02, 0.40, 0.02, "F2"));
            m._items.Add(DebugItem.MakeNumber(CoronaFalloffId, "コロナの減衰 (指数)",
                coronaFalloff, 0.5, 6.0, 0.25, "F2"));

            // **bloom は 9-4 で決め直す。** Step 6 の値は実行時に効いていなかった。
            // **上限は 6.0。** 3.00 で確定したが、それは当時の上限 3.0 に
            // 張り付いた値だった。足りているのかを確かめられるよう広げてある。
            m._items.Add(DebugItem.MakeNumber(BloomIntensityId, "bloom 強度",
                bloomIntensity, 0.0, 6.0, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(BloomThresholdId, "bloom しきい値",
                bloomThreshold, 0.0, 3.0, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(BloomScatterId, "bloom 拡散",
                bloomScatter, 0.0, 1.0, 0.05, "F2"));
            // ---- 音量 (Step 10-2) ----
            m._items.Add(DebugItem.MakeNumber(MasterVolumeId, "音量 Master",
                masterVolume, AudioMix.MinVolume, AudioMix.MaxVolume, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(EngineVolumeId, "音量 Engine",
                engineVolume, AudioMix.MinVolume, AudioMix.MaxVolume, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(CockpitVolumeId, "音量 Cockpit",
                cockpitVolume, AudioMix.MinVolume, AudioMix.MaxVolume, 0.05, "F2"));
            m._items.Add(DebugItem.MakeNumber(SfxVolumeId, "音量 SFX",
                sfxVolume, AudioMix.MinVolume, AudioMix.MaxVolume, 0.05, "F2"));

            m._items.Add(DebugItem.MakeNumber(ShakeId, "微振動の振幅 [rad]",
                shakeAmplitude, 0.0, 5.0e-3, 2.5e-4, "E3"));

            return m;
        }

        public static string BodyId(string bodyName, string part) => "body." + bodyName + "." + part;

        public DebugItem Find(string id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id == id)
                {
                    return _items[i];
                }
            }

            return null;
        }

        public bool BoolOf(string id)
        {
            DebugItem item = Find(id);
            return item != null && item.BoolValue;
        }

        public double NumberOf(string id)
        {
            DebugItem item = Find(id);
            return item != null ? item.Value : 0.0;
        }

        /// <summary>選ばれている段の番号。0 = なし。</summary>
        public int SoloIndex
        {
            get
            {
                DebugItem item = Find(SoloId);
                return item != null ? item.Index : 0;
            }
        }

        /// <summary>
        /// その段を描くか。**「1 段だけ表示」が優先される (排他)。**
        /// tierIndex は 1=Deep / 2=Near / 3=Nearfield / 4=Cockpit。
        /// </summary>
        public bool TierVisible(int tierIndex, string toggleId)
        {
            int solo = SoloIndex;
            if (solo != 0)
            {
                return tierIndex == solo;
            }

            return BoolOf(toggleId);
        }

        public void SetOpen(bool open) => IsOpen = open;

        public void ToggleOpen() => IsOpen = !IsOpen;

        /// <summary>カーソルを直接置く。範囲外は丸める。</summary>
        public void SetCursor(int index)
        {
            if (_items.Count == 0)
            {
                return;
            }

            if (index < 0) { index = 0; }
            if (index >= _items.Count) { index = _items.Count - 1; }
            Cursor = index;
        }

        public void MoveCursor(int delta)
        {
            if (_items.Count == 0)
            {
                return;
            }

            Cursor = ((Cursor + delta) % _items.Count + _items.Count) % _items.Count;
        }

        public void Adjust(int delta)
        {
            DebugItem item = Current;
            if (item != null)
            {
                item.Adjust(delta);
            }
        }

        public void ToggleCurrent()
        {
            DebugItem item = Current;
            if (item != null && item.Kind == DebugItemKind.Toggle)
            {
                item.BoolValue = !item.BoolValue;
            }
        }

        /// <summary>R。全項目を既定へ。</summary>
        public void ResetAll()
        {
            foreach (DebugItem item in _items)
            {
                item.Reset();
            }
        }

        /// <summary>
        /// シナリオ切替時。**トグルと選択だけ戻し、数値は保持する。**
        /// earth-close-day で決めた値を terminator や night でも確かめたいので、
        /// 切り替えるたびに叩き直しになると目的を果たせない。
        /// </summary>
        public void ResetToggles()
        {
            foreach (DebugItem item in _items)
            {
                if (item.Kind != DebugItemKind.Number)
                {
                    item.Reset();
                }
            }
        }

        public IReadOnlyList<DebugItem> ChangedItems()
        {
            var list = new List<DebugItem>();
            foreach (DebugItem item in _items)
            {
                if (item.IsChanged)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        /// <summary>閉じたときにログへ出す文字列。変更された項目だけ。</summary>
        public string BuildChangeLog()
        {
            IReadOnlyList<DebugItem> changed = ChangedItems();
            if (changed.Count == 0)
            {
                return "[DebugPanel] 既定から変更された項目はありません。";
            }

            var sb = new StringBuilder();
            sb.AppendLine("[DebugPanel] 既定から変更された項目 " + changed.Count + " 件:");
            foreach (DebugItem item in changed)
            {
                sb.AppendLine("  " + item.Describe());
            }

            return sb.ToString().TrimEnd();
        }
    }
}
