using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// レイヤーを ProjectSettings/TagManager.asset へ追加する。
    /// シーンを毎回生成する (決定 D-20) 以上、レイヤーもコードから用意しないと
    /// 別の環境で clone したときに再現できない。
    /// </summary>
    public static class LayerUtility
    {
        /// <summary>指定名のレイヤーがあればその番号を、無ければ空きに作って番号を返す。</summary>
        public static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0)
            {
                return existing;
            }

            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            // 0..7 は Unity の組み込み。8 以降が使える。
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                    return i;
                }
            }

            throw new System.InvalidOperationException($"空きレイヤーが無いので {layerName} を作れない。");
        }
    }
}
