using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **頭姿勢を段ごとのスケールで配る (Step 12-2)。**
    ///
    /// ■ 直している問題
    /// XR はトラッキング位置も眼間も**メートルで**カメラに掛ける。段のスケールを
    /// 考えないので、12-1b の実測では眼間が 4 段とも同じ値で出ていた:
    ///
    ///   実機     6.239196E-2 units -> コックピット段 62.4 mm（正しい）/ 外 3 段 62.4 m 相当
    ///   MockHMD  2.200001E-2 units -> 同じく 22 mm / 22 m 相当
    ///
    /// **コックピット段は元から正しい。壊れているのは外 3 段。**
    /// Nearfield は近距離を担当するので、ステーションが左右でまったく違う位置に出る。
    ///
    /// ■ やり方（XR Origin のスケール）
    /// 各段のカメラを `XrEyeAnchor_*` の下に移し、**親のスケールを段のスケール**
    /// （コックピット 1.0 / 外 3 段 0.001）にする。メートルで入ってくる量が
    /// そのまま s 倍されるので、外 3 段では眼間が 6.24e-5 units になる。
    ///
    /// 計算は **12-1 の `StereoGeometry.CameraLocal` をそのまま使う。**
    /// ここで新しい式を書かない。
    ///
    /// ■ **XR のときだけ作る。**
    /// `XrBoot` が XR を立ち上げたときだけ生成される。平面のカメラ配置には
    /// 一切触らない（平面の 36 枚が 1 px も動かないこと）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrEyeRig : MonoBehaviour
    {
        public const string NamePrefix = "XrEyeAnchor_";

        /// <summary>
        /// **対照用 (Step 12-2)。** 付けると配布を行わない。
        /// 配布前後を同じ exe で撮り比べるための口で、既定は配布する。
        /// </summary>
        public const string DisableArg = "-xrNoEyeRig";

        /// <summary>1 段ぶんの取り付け。</summary>
        public sealed class Anchor
        {
            public string Stage = string.Empty;
            public Camera Camera;
            public Transform Transform;
            public double Scale;

            /// <summary>頭が原点のときの、親（船）から見たカメラの位置。</summary>
            public Vector3 RestPosition;

            public override string ToString()
                => $"{Stage}: s={Scale} / anchor={Transform.localPosition} / rest={RestPosition}";
        }

        public static IReadOnlyList<Anchor> Anchors => _anchors;

        static List<Anchor> _anchors = new List<Anchor>();

        void Start()
        {
            if (StandaloneCapture.HasArg(DisableArg))
            {
                Debug.Log("[XrEyeRig] " + DisableArg + " が付いているので配布しない（対照）");
                _anchors = new List<Anchor>();
                return;
            }

            var stack = FindAnyObjectByType<CameraStackController>();
            if (stack == null)
            {
                Debug.LogWarning("[XrEyeRig] カメラスタックが見つからない。配布していない。");
                return;
            }

            _anchors = Build(stack);
            foreach (Anchor anchor in _anchors)
            {
                Debug.Log("[XrEyeRig] " + anchor);
            }
        }

        /// <summary>
        /// 各段のカメラを、段のスケールを持つ親の下へ移す。**冪等**
        /// （既に移してあれば作り直さない）。
        ///
        /// カメラのワールド位置は変えない。頭が原点のときの絵は配布前と同じ。
        /// </summary>
        public static List<Anchor> Build(CameraStackController stack)
        {
            var anchors = new List<Anchor>();
            if (stack == null)
            {
                return anchors;
            }

            (string Stage, Camera Camera, double Scale)[] spec =
            {
                ("Deep", stack.Deep, StereoGeometry.WorldStageScale),
                ("Near", stack.Near, StereoGeometry.WorldStageScale),
                ("Nearfield", stack.Nearfield, StereoGeometry.WorldStageScale),
                ("Cockpit", stack.Cockpit, StereoGeometry.CockpitStageScale),
            };

            foreach ((string stage, Camera camera, double scale) in spec)
            {
                if (camera == null)
                {
                    continue;
                }

                anchors.Add(Attach(stage, camera, scale));
            }

            return anchors;
        }

        static Anchor Attach(string stage, Camera camera, double scale)
        {
            Transform cameraTransform = camera.transform;
            Transform parent = cameraTransform.parent;

            // 既に取り付け済みなら作り直さない。
            if (parent != null && parent.name == NamePrefix + stage)
            {
                return Describe(stage, camera, parent, scale);
            }

            var go = new GameObject(NamePrefix + stage);
            Transform anchor = go.transform;

            // **カメラの元のローカル姿勢を親へ移す。** カメラ側は原点に置く。
            // こうするとカメラのワールド姿勢は変わらないまま、XR が書き込む
            // メートル量だけが親のスケールを通る。
            anchor.SetParent(parent, false);
            anchor.localPosition = cameraTransform.localPosition;
            anchor.localRotation = cameraTransform.localRotation;
            anchor.localScale = Vector3.one * (float)scale;

            cameraTransform.SetParent(anchor, false);
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
            cameraTransform.localScale = Vector3.one;

            return Describe(stage, camera, anchor, scale);
        }

        static Anchor Describe(string stage, Camera camera, Transform anchor, double scale)
            => new Anchor
            {
                Stage = stage,
                Camera = camera,
                Transform = anchor,
                Scale = scale,
                RestPosition = anchor.parent == null
                    ? camera.transform.position
                    : anchor.parent.InverseTransformPoint(camera.transform.position),
            };

        /// <summary>
        /// **頭の動きを模す (検証用)。** XR がやっていること——カメラの
        /// ローカル位置にメートルを書き込む——を、そのまま再現する。
        /// 実行時の経路では XR が書くので、ここは呼ばない。
        /// </summary>
        public static void SimulateHead(IReadOnlyList<Anchor> anchors, Vector3 headMeters)
        {
            if (anchors == null)
            {
                return;
            }

            foreach (Anchor anchor in anchors)
            {
                if (anchor?.Camera != null)
                {
                    anchor.Camera.transform.localPosition = headMeters;
                }
            }
        }

        /// <summary>
        /// 頭が `headMeters` のときの、親（船）から見たカメラの位置。**実測値。**
        /// 期待値は `StereoGeometry.CameraLocal(head, scale).Position` で出す。
        /// </summary>
        public static Vector3 PositionInParent(Anchor anchor)
        {
            if (anchor?.Camera == null || anchor.Transform == null)
            {
                return Vector3.zero;
            }

            Transform parent = anchor.Transform.parent;
            return parent == null
                ? anchor.Camera.transform.position
                : parent.InverseTransformPoint(anchor.Camera.transform.position);
        }
    }
}
