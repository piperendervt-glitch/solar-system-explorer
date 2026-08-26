using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>セーブの読み書き (Step 7)。</summary>
    public sealed class SaveTests
    {
        [Test]
        public void 書いたステーション名がそのまま読める()
        {
            string json = SaveCodec.Serialize("MARS STATION");
            Debug.Log($"[Step7] JSON: {json}");

            Assert.That(SaveCodec.TryParse(json, out string name), Is.True);
            Assert.That(name, Is.EqualTo("MARS STATION"));
        }

        [Test]
        public void 引用符とバックスラッシュを含む名前も往復する()
        {
            const string awkward = "A \"B\" \\ C";
            string json = SaveCodec.Serialize(awkward);
            Debug.Log($"[Step7] 面倒な名前: {json}");

            Assert.That(SaveCodec.TryParse(json, out string name), Is.True);
            Assert.That(name, Is.EqualTo(awkward));
        }

        [Test]
        public void 壊れたJSONは例外を投げずfalseを返す()
        {
            string[] broken =
            {
                null,
                "",
                "{",
                "not json at all",
                "{\"station\"}",                  // 値が無い
                "{\"station\":}",                 // コロンの後が空
                "{\"station\":123}",              // 文字列でない
                "{\"station\":\"閉じていない",     // 閉じクォート無し
                "{\"station\":\"末尾が\\",        // エスケープが閉じない
                "{\"other\":\"MARS STATION\"}",   // 別のキーだけ
            };

            foreach (string json in broken)
            {
                bool ok = SaveCodec.TryParse(json, out string name);
                Debug.Log($"[Step7] 壊れ入力 '{json ?? "(null)"}' -> {ok} / '{name ?? "(null)"}'");
                Assert.That(ok, Is.False, $"'{json}' を読めてしまった");
            }
        }

        [Test]
        public void 前後に空白や他のキーがあっても読める()
        {
            Assert.That(SaveCodec.TryParse("{ \"station\" : \"EARTH STATION\" }", out string a), Is.True);
            Assert.That(a, Is.EqualTo("EARTH STATION"));

            Assert.That(SaveCodec.TryParse("{\"v\":1,\"station\":\"MARS STATION\"}", out string b), Is.True);
            Assert.That(b, Is.EqualTo("MARS STATION"));
        }

        // ---- 番号への解決 ----

        [Test]
        public void 保存された名前がステーション番号に解決される()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            for (int i = 0; i < model.Stations.Count; i++)
            {
                string json = SaveCodec.Serialize(model.Stations[i].Name);
                int resolved = SaveResolver.Resolve(json, model);
                Debug.Log($"[Step7] '{model.Stations[i].Name}' -> 番号 {resolved}");
                Assert.That(resolved, Is.EqualTo(i));
            }
        }

        [Test]
        public void 未知の名前と壊れたJSONは地球ステーションに倒れる()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();

            string[] inputs =
            {
                null,
                "",
                "壊れている",
                "{\"station\":\"PLUTO STATION\"}", // 知らない名前
                "{\"station\":\"\"}",              // 空の名前
            };

            foreach (string json in inputs)
            {
                int resolved = SaveResolver.Resolve(json, model);
                Debug.Log($"[Step7] '{json ?? "(null)"}' -> 番号 {resolved} " +
                          $"({model.Stations[resolved].Name})");
                Assert.That(resolved, Is.EqualTo(SaveResolver.DefaultStationIndex));
            }

            Assert.That(model.Stations[SaveResolver.DefaultStationIndex].Name,
                Does.Contain("Earth"), "既定は地球ステーション");
        }
    }
}
