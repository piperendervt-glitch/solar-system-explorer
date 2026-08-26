using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Unity
{
    /// <summary>
    /// カメラスタック (docs/01-architecture.md §3-4 / 決定 D-6)。
    ///
    /// **2 段で開始する。3 段目は Z ファイトが実測で出てから足す。**
    ///   Deep      (Base)    near 500  / far 1.2e4 : プロキシ殻だけ (殻は 1,000〜10,000)
    ///   Near      (Overlay) near 100  / far 1.5e5 : 実スケール天体
    ///   Nearfield (Overlay) near 0.01 / far 100   : ステーション・船 (実寸 0.1〜0.5 units)
    ///   Cockpit   (Overlay) near 0.1  / far 100   : コックピット内装 (実寸 2 m x 1000 倍)
    ///
    /// **奥から手前の順に描き、Overlay は深度をクリアする。**
    /// 深度をクリアするので段どうしで Z 比較は起きない。後の段が必ず上に来る。
    /// レイヤーのカリングマスクを排他にしてあるので、同じオブジェクトが
    /// 2 つの段に描かれることもない (二重描画なし)。
    /// 段の担当範囲は距離で連続していて隙間が無いので、天体が消えることもない。
    ///
    /// Step 4 で 3 段目を足した。コックピットは 0.002 units しかなく、
    /// Near の near=1 (1 km) では丸ごとクリップされるため。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraStackController : MonoBehaviour
    {
        public const float DeepNearClip = 500f;
        public const float DeepFarClip = 12000f;
        // Step 3b: 実スケールメッシュ (火星まで 5e4 units、半径 3389.5) が入るので広げる。
        // Step 5: ステーション段を足したので、実スケール天体は 100 units から。
        public const float NearNearClip = 100f;
        public const float NearFarClip = 150000f;
        // Step 5: ステーションは実寸 0.5 units。
        public const float NearfieldNearClip = 0.01f;
        public const float NearfieldFarClip = 100f;
        // Step 4: コックピットは実寸 2 m = 0.002 units。
        //
        // **Unity は Camera.nearClipPlane を 0.01 で下限クランプする。**
        // near=0.0001 を代入しても実際には 0.01 になり (実測)、
        // 0.002 units のコックピットは丸ごと near の内側に入って消えた。
        //
        // そこでコックピットだけ 1000 倍の「描画空間」で組む。
        // Cockpit 段は専用レイヤーで深度もクリアするので、他の段との
        // 距離関係は意味を持たない。寸法と距離を同じ倍率で拡げれば
        // 見かけの角度は 1:1 のまま変わらない。
        public const float CockpitRenderScale = 1000f;
        public const float CockpitNearClip = 0.0001f * CockpitRenderScale;  // = 0.1
        public const float CockpitFarClip = 0.1f * CockpitRenderScale;      // = 100
        public const float VerticalFovDegrees = 60f;

        [SerializeField] Camera _deep;
        [SerializeField] Camera _near;
        [SerializeField] Camera _nearfield;
        [SerializeField] Camera _cockpit;

        public Camera Deep => _deep;
        public Camera Near => _near;
        public Camera Nearfield => _nearfield;
        public Camera Cockpit => _cockpit;

        public void Bind(Camera deep, Camera near, Camera nearfield, Camera cockpit)
        {
            _deep = deep;
            _near = near;
            _nearfield = nearfield;
            _cockpit = cockpit;
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
            // **Skybox のままにする。** ここを SolidColor にすると星空が描かれない
            // (Step 6 で実際に踏んだ)。色をクリアするのは Base だけなので、
            // スカイボックスを描くのはこの段だけ。
            _deep.clearFlags = CameraClearFlags.Skybox;
            _deep.backgroundColor = Color.black;
            _deep.depth = 0;

            _near.fieldOfView = VerticalFovDegrees;
            _near.nearClipPlane = NearNearClip;
            _near.farClipPlane = NearFarClip;
            _near.depth = 1;

            if (_nearfield != null)
            {
                _nearfield.fieldOfView = VerticalFovDegrees;
                _nearfield.nearClipPlane = NearfieldNearClip;
                _nearfield.farClipPlane = NearfieldFarClip;
                _nearfield.depth = 2;
            }

            if (_cockpit != null)
            {
                _cockpit.fieldOfView = VerticalFovDegrees;
                _cockpit.nearClipPlane = CockpitNearClip;
                _cockpit.farClipPlane = CockpitFarClip;
                _cockpit.depth = 3;
            }

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

            if (_nearfield != null)
            {
                UniversalAdditionalCameraData nearfieldData = _nearfield.GetUniversalAdditionalCameraData();
                nearfieldData.renderType = CameraRenderType.Overlay;
                deepData.cameraStack.Add(_nearfield);
            }

            if (_cockpit != null)
            {
                UniversalAdditionalCameraData cockpitData = _cockpit.GetUniversalAdditionalCameraData();
                cockpitData.renderType = CameraRenderType.Overlay;
                deepData.cameraStack.Add(_cockpit);
            }
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

            if (_nearfield != null)
            {
                deepData.cameraStack.Add(_nearfield);
            }

            if (_cockpit != null)
            {
                deepData.cameraStack.Add(_cockpit);
            }
        }

        /// <summary>
        /// スタックを空にして Base (Deep) だけを描く。
        /// 星空だけを取り出して比べたいときに使う。戻すときは Configure() を呼ぶ。
        /// </summary>
        public void ClearStack()
        {
            if (_deep == null)
            {
                return;
            }

            _deep.GetUniversalAdditionalCameraData().cameraStack.Clear();
        }

        /// <summary>コックピットの段だけ外す。天体だけのスクショを撮るのに使う。</summary>
        public void SetCockpitEnabled(bool enabled)
        {
            if (_deep == null || _cockpit == null)
            {
                return;
            }

            UniversalAdditionalCameraData deepData = _deep.GetUniversalAdditionalCameraData();
            deepData.cameraStack.Remove(_cockpit);
            if (enabled)
            {
                deepData.cameraStack.Add(_cockpit);
            }
        }
    }
}
