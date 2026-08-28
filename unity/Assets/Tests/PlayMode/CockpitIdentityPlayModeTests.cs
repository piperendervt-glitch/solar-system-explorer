using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// どの定義で組まれたかがシーンから読めること (Step 11-0c)。
    ///
    /// 計画書 11-1 の PlayMode テストは「レンダラー数が箱より多い」で差し替わりを
    /// 判定する形になっているが、**これは間接的すぎる。**
    /// メッシュの構成が変われば箱より少ないこともあるし、多くても別のアセットかも
    /// しれない。**Id が読めれば「hirez-sample で組まれた」と直接言える。**
    /// </summary>
    public sealed class CockpitIdentityPlayModeTests
    {
        CockpitIdentity _identity;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-cockpit.save.json");
            SaveFile.Delete();
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _identity = Object.FindAnyObjectByType<CockpitIdentity>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        [UnityTest]
        public IEnumerator 組まれた定義がシーンに残っている()
        {
            yield return null;

            Assert.That(_identity, Is.Not.Null, "CockpitIdentity がシーンに無い");
            Assert.That(_identity.DefinitionId, Is.Not.Null.And.Not.Empty);

            Debug.Log(string.Format(
                "  [Step11-0] コックピット: 要求 {0} / 実際 {1} / フォールバック {2}",
                _identity.RequestedId, _identity.DefinitionId, _identity.FellBackToBox));
        }

        [UnityTest]
        public IEnumerator 要求と記録が食い違わない()
        {
            yield return null;

            // 11-2a で `CockpitBuilder` がプレハブを置くようになったので、
            // 要求は hirez-sample に戻した。**取り込みの有無でどちらにもなりうる**ので、
            // ここで縛るのは「記録が実態と食い違わないこと」。
            //   取り込み済み  -> hirez-sample / フォールバックしていない
            //   取り込み無し  -> box / フォールバックした
            if (_identity.FellBackToBox)
            {
                Assert.That(_identity.DefinitionId, Is.EqualTo(CockpitDefinition.BoxId));
            }
            else
            {
                Assert.That(_identity.DefinitionId, Is.EqualTo(_identity.RequestedId));
            }
        }

        [UnityTest]
        public IEnumerator フォールバックしたときは要求元が分かる()
        {
            yield return null;

            if (_identity.FellBackToBox)
            {
                Assert.That(_identity.RequestedId, Is.Not.EqualTo(_identity.DefinitionId),
                            "落ちたのに要求元と同じ Id になっている");
                Assert.That(_identity.Describe(), Does.Contain(_identity.RequestedId),
                            "落ちた理由に要求元が出ていない");
            }
            else
            {
                Assert.That(_identity.RequestedId, Is.EqualTo(_identity.DefinitionId));
                Assert.That(_identity.Describe(), Is.EqualTo(_identity.DefinitionId));
            }
        }

        [UnityTest]
        public IEnumerator デバッグHUDに組まれた定義が出る()
        {
            yield return null;

            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            Assert.That(overlay, Is.Not.Null);
            overlay.Visible = true;

            string text = overlay.BuildText();

            // **実機で取り違えに気づけるようにするため。**
            Assert.That(text, Does.Contain("コックピット"), "HUD にコックピットの行が無い");
            Assert.That(text, Does.Contain(_identity.DefinitionId),
                        "組まれた定義が HUD に出ていない");
        }
    }
}
