using System.Text;
using SolarSystem.Core;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// プレハブの寸法から**目の位置の初期値**を出す (Step 11-2b)。
    ///
    /// ■ **決めるのは人。ここは出発点を出すだけ。**
    /// 座席の位置はアセットのデモシーンから拾えない（11-1a で削除した）ので、
    /// bounds の中心を初期値にする。**「中心」に根拠は無い**が、恣意的な当て推量よりは
    /// 追跡できる。実際の値は exe の F4 で振って決め、決まってから
    /// `CockpitDefinition.EyeLocal` に定数として入れる。
    ///
    /// ■ 単位
    /// コックピットは 1000 倍の描画空間にあり、そこでは **1 m = 1 unit**
    /// （`CameraStackController.CockpitRenderScale`）。**ここで出す値はメートル。**
    /// </summary>
    public static class CockpitBoundsSolver
    {
        /// <summary>レンダラーの AABB を合成した bounds。レンダラーが無ければ null。</summary>
        public static Bounds? LocalBounds(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            // **プレハブのルート基準に直す。** 描画器の bounds はワールド基準で返るので、
            // ルートの逆変換を掛けないとプレハブの中での位置にならない。
            Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;
            var combined = new Bounds(toLocal.MultiplyPoint3x4(renderers[0].bounds.center), Vector3.zero);

            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                Vector3 min = b.min;
                Vector3 max = b.max;

                // 8 隅を変換してから包む。中心と大きさだけでは回転を拾えない。
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? min.x : max.x,
                        (i & 2) == 0 ? min.y : max.y,
                        (i & 4) == 0 ? min.z : max.z);

                    combined.Encapsulate(toLocal.MultiplyPoint3x4(corner));
                }
            }

            return combined;
        }

        /// <summary>窓（ガラス）だけの bounds。無ければ null。</summary>
        public static Bounds? WindowBounds(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;
            Bounds? combined = null;

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                bool isGlass = false;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.name.Contains(
                            SolarSystem.Unity.CockpitMetrics.GlassMaterialKeyword))
                    {
                        isGlass = true;
                        break;
                    }
                }

                if (!isGlass)
                {
                    continue;
                }

                Vector3 center = toLocal.MultiplyPoint3x4(renderer.bounds.center);
                var b = new Bounds(center, renderer.bounds.size);
                combined = combined.HasValue ? Encapsulated(combined.Value, b) : b;
            }

            return combined;
        }

        static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        /// <summary>座席のレンダラー。**射出ハンドルは座席ではない。**</summary>
        public static Bounds? SeatBounds(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.Contains("Seat") || renderer.name.Contains("Ejector"))
                {
                    continue;
                }

                return new Bounds(toLocal.MultiplyPoint3x4(renderer.bounds.center),
                                  renderer.bounds.size);
            }

            return null;
        }

        /// <summary>
        /// 目の位置の初期値（メートル）。**座席の位置に、窓の高さ。**
        ///
        /// ■ 決め方（11-2b で 3 回直した。すべて実測に基づく）
        /// 1. 全体 bounds の中心 -> **胴体の中**で、機体の外殻しか写らなかった
        /// 2. 窓の中心 -> **窓の内側**なので目の前に内装が来た
        /// 3. 窓の後端 -> 窓は前後に長く、後端は操縦席の後ろだった
        /// 4. いまは**座席の水平位置に、窓の中心の高さ**。
        ///    座席 (0, -0.074, -1.436) の 0.50 m 上に窓の中心 (0, 0.429, -1.515) があり、
        ///    **窓は座席の真上を覆っている。** そこが操縦者の頭の位置に最も近い
        ///
        /// **座席が見つからないアセットでは窓の中心へ落とす。**
        /// どれも出発点でしかない。決めるのは F4 を振る人。
        /// </summary>
        public static Vec3d SuggestEye(GameObject prefab, Bounds fullBounds,
                                       CockpitDefinition definition)
        {
            Bounds? window = WindowBounds(prefab);
            Bounds? seat = SeatBounds(prefab);

            if (!window.HasValue && !seat.HasValue)
            {
                Vector3 c = fullBounds.center;
                return new Vec3d(c.x, c.y, c.z);
            }

            Vec3d u = definition != null ? definition.EyeUp : new Vec3d(0.0, 1.0, 0.0);
            var up = new Vector3((float)u.X, (float)u.Y, (float)u.Z);
            up = up.sqrMagnitude < 1e-6f ? Vector3.up : up.normalized;

            if (!seat.HasValue)
            {
                Vector3 c = window.Value.center;
                return new Vec3d(c.x, c.y, c.z);
            }

            Vector3 eye = seat.Value.center;
            if (window.HasValue)
            {
                // 座席から**上方向にだけ**動かして、窓の中心の高さに合わせる。
                // 前後・左右は座席のまま。
                float rise = Vector3.Dot(window.Value.center - eye, up);
                eye += up * rise;
            }

            return new Vec3d(eye.x, eye.y, eye.z);
        }

        /// <summary>実測値をログに出す。**batchmode の観測経路。**</summary>
        public static void Log(CockpitDefinition definition, GameObject prefab)
        {
            Bounds? bounds = LocalBounds(prefab);
            if (bounds == null)
            {
                Debug.Log($"[CockpitBounds] {definition.Id}: レンダラーが無いので寸法を出せない");
                return;
            }

            Bounds b = bounds.Value;
            Vec3d suggested = SuggestEye(prefab, b, definition);
            bool fromDefinition = definition.EyeLocal.HasValue;
            Vec3d eye = definition.EyeLocal ?? suggested;

            var sb = new StringBuilder();
            sb.AppendLine($"[CockpitBounds] {definition.Id} の寸法（メートル。1 m = 1 unit）");
            sb.AppendLine($"  size   {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3}"
                          + $"  (幅 x 高さ x 奥行き)");
            sb.AppendLine($"  center ({b.center.x:F3}, {b.center.y:F3}, {b.center.z:F3})");
            sb.AppendLine($"  min    ({b.min.x:F3}, {b.min.y:F3}, {b.min.z:F3})");
            sb.AppendLine($"  max    ({b.max.x:F3}, {b.max.y:F3}, {b.max.z:F3})");
            sb.AppendLine($"  目の初期値（座席の位置に窓の高さ）"
                          + $" ({suggested.X:F3}, {suggested.Y:F3}, {suggested.Z:F3})");
            // **窓の位置は機首方向の手掛かり (11-2c)。**
            // 目の位置から見て窓が後ろ側にあるなら、そのアセットは Z+ を向いていない。
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                bool isGlass = false;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.name.Contains(
                            SolarSystem.Unity.CockpitMetrics.GlassMaterialKeyword))
                    {
                        isGlass = true;
                        break;
                    }
                }

                if (!isGlass)
                {
                    continue;
                }

                Vector3 local = prefab.transform.InverseTransformPoint(renderer.bounds.center);
                sb.AppendLine($"  窓 {renderer.name}: 中心 ({local.x:F3}, {local.y:F3}, {local.z:F3})"
                              + $" / 大きさ {renderer.bounds.size.x:F3} x {renderer.bounds.size.y:F3}"
                              + $" x {renderer.bounds.size.z:F3}");
            }

            // **上方向の根拠 (11-2c)。** 座席・操縦桿・計器の位置関係から上を決める。
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                string n = renderer.name;
                if (!n.Contains("Seat") && !n.Contains("Joystick") && !n.Contains("Screen-"))
                {
                    continue;
                }

                Vector3 local = prefab.transform.InverseTransformPoint(renderer.bounds.center);
                sb.AppendLine($"  部品 {n}: 中心 ({local.x:F3}, {local.y:F3}, {local.z:F3})");
            }

            sb.Append(fromDefinition
                ? $"  **採用したのは定義側の値** ({eye.X:F3}, {eye.Y:F3}, {eye.Z:F3})"
                : "  **採用したのは初期値**（定義側は未定。F4 で決めてから定数にする）");

            Debug.Log(sb.ToString());
        }
    }
}
