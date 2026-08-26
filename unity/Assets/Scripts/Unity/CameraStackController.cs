using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Unity
{
    /// <summary>
    /// カメラスタック (docs/01-architecture.md §3-4 / 決定 D-6)。
    ///
    /// **2 段で開始する。3 段目は Z ファイトが実測で出てから足す。**
    ///   Deep (Base)    near 500 / far 12000  : プロキシ殻だけ。殻は 1,000〜10,000 に載る
    ///   Near (Overlay) near 1 / far 1.5e5    : 実スケール天体。深度をクリアして上に重ねる
    ///
    /// 奥から手前へ描くので、Near が Deep を覆う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraStackController : MonoBehaviour
    {
        public const float DeepNearClip = 500f;
        public const float DeepFarClip = 12000f;
        // Step 3b: 実スケールメッシュ (火星まで 5e4 units、半径 3389.5) が入るので広げる。
        public const float NearNearClip = 1f;
        public const float NearFarClip = 150000f;
        public const float VerticalFovDegrees = 60f;

        [SerializeField] Camera _deep;
        [SerializeField] Camera _near;

        public Camera Deep => _deep;
        public Camera Near => _near;

        public void Bind(Camera deep, Camera near)
        {
            _deep = deep;
            _near = near;
        }

        /// <summary>
        /// スタックを組み直す。シーン生成時と、Editor から明示的に呼ぶ。
        /// Awake に頼らないのは、batchmode の Editor では Awake が走らないため。
        /// </summary>
        public void Configure()
        {
            if (_deep == null || _near == null)
            {
                return;
            }

            _deep.fieldOfView = VerticalFovDegrees;
            _deep.nearClipPlane = DeepNearClip;
            _deep.farClipPlane = DeepFarClip;
            _deep.clearFlags = CameraClearFlags.SolidColor;
            _deep.backgroundColor = Color.black;
            _deep.depth = 0;

            _near.fieldOfView = VerticalFovDegrees;
            _near.nearClipPlane = NearNearClip;
            _near.farClipPlane = NearFarClip;
            _near.depth = 1;

            UniversalAdditionalCameraData deepData = _deep.GetUniversalAdditionalCameraData();
            deepData.renderType = CameraRenderType.Base;
            deepData.cameraStack.Clear();

            UniversalAdditionalCameraData nearData = _near.GetUniversalAdditionalCameraData();
            nearData.renderType = CameraRenderType.Overlay;
            // clearDepth は URP 17 では読み取り専用 (m_ClearDepth の既定値が true)。
            // Overlay は既定で深度をクリアするので、ここで触る必要はない。
            // 既定が変わったらここで気づけるよう検査だけしておく。
            if (!nearData.clearDepth)
            {
                Debug.LogWarning("[CameraStack] Overlay の clearDepth が false。近景が奥に隠れる。");
            }

            deepData.cameraStack.Add(_near);
        }

        /// <summary>
        /// Overlay (Near) をスタックから外す/戻す。
        /// 外すとプロキシ殻だけの絵になるので、実スケール引き渡しの前後比較に使う。
        /// </summary>
        public void SetOverlayEnabled(bool enabled)
        {
            if (_deep == null || _near == null)
            {
                return;
            }

            UniversalAdditionalCameraData deepData = _deep.GetUniversalAdditionalCameraData();
            deepData.cameraStack.Clear();
            if (enabled)
            {
                deepData.cameraStack.Add(_near);
            }
        }
    }
}
