using System.Text;

namespace SolarSystem.Core
{
    /// <summary>
    /// セーブデータの読み書き (Step 7)。
    ///
    /// 中身は「最後にドッキングしたステーション名」1 個だけ。
    /// JSON はこの 1 キーしか扱わないので、手書きの最小実装で足りる
    /// (Core は UnityEngine を参照できないので JsonUtility が使えない)。
    ///
    /// **壊れた入力では決して例外を投げない。** false を返して呼び手に任せる。
    /// 要件: 壊れた JSON・未知の名前でも落ちず、地球から始める。
    /// </summary>
    public static class SaveCodec
    {
        public const string StationKey = "station";

        public static string Serialize(string stationName)
        {
            var builder = new StringBuilder();
            builder.Append("{\"").Append(StationKey).Append("\":\"");
            AppendEscaped(builder, stationName ?? string.Empty);
            builder.Append("\"}");
            return builder.ToString();
        }

        /// <summary>読めたら true。壊れていれば false を返し、例外は投げない。</summary>
        public static bool TryParse(string json, out string stationName)
        {
            stationName = null;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            // "station" というキーを探す。前後の空白や他のキーは読み飛ばす。
            string quotedKey = "\"" + StationKey + "\"";
            int keyIndex = json.IndexOf(quotedKey, System.StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return false;
            }

            int i = keyIndex + quotedKey.Length;
            i = SkipWhitespace(json, i);
            if (i >= json.Length || json[i] != ':')
            {
                return false;
            }

            i = SkipWhitespace(json, i + 1);
            if (i >= json.Length || json[i] != '"')
            {
                return false;
            }

            i++; // 開きクォートの次から
            var value = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];

                if (c == '\\')
                {
                    // エスケープ。閉じないまま終わっていたら壊れている。
                    if (i + 1 >= json.Length)
                    {
                        return false;
                    }

                    char next = json[i + 1];
                    if (next != '"' && next != '\\')
                    {
                        return false; // 対応しないエスケープは壊れ扱い
                    }

                    value.Append(next);
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    stationName = value.ToString();
                    return true;
                }

                value.Append(c);
                i++;
            }

            return false; // 閉じクォートが無い
        }

        static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
            {
                i++;
            }

            return i;
        }

        static void AppendEscaped(StringBuilder builder, string value)
        {
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                {
                    builder.Append('\\');
                }

                builder.Append(c);
            }
        }
    }

    /// <summary>
    /// セーブの中身をステーション番号へ解決する (Step 7)。
    ///
    /// 読めない / 知らない名前なら **地球ステーション (番号 0)** を返す。
    /// 「落ちずに地球から始める」は、この 1 箇所で決まる。
    /// </summary>
    public static class SaveResolver
    {
        /// <summary>既定の開始地点。地球ステーション。</summary>
        public const int DefaultStationIndex = 0;

        public static int Resolve(string json, SolarSystemModel model)
        {
            if (model == null || model.Stations == null || model.Stations.Count == 0)
            {
                return DefaultStationIndex;
            }

            if (!SaveCodec.TryParse(json, out string name) || string.IsNullOrEmpty(name))
            {
                return DefaultStationIndex;
            }

            for (int i = 0; i < model.Stations.Count; i++)
            {
                if (string.Equals(model.Stations[i].Name, name, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return DefaultStationIndex; // 知らない名前
        }
    }
}
