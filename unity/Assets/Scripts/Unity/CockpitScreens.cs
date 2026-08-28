using System;
using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 計器をコックピットの画面へ映す (Step 11-3)。
    ///
    /// ■ **マテリアルを複製しない。**
    /// 5 面は 2 枚の共有マテリアル（`CockpitEquipments_Screens` / `_TargetScreens`）を
    /// 使っているが、**それぞれ別の UV 矩形**を占めている（実測）。面ごとに
    /// `MaterialPropertyBlock` で `_BaseMap` / `_EmissionMap` / `_BaseMap_ST` を渡せば、
    /// 共有のまま別の RenderTexture を貼れる。
    /// **URP Lit の CBUFFER にあるのは `_BaseMap_ST` だけ**で、発光も同じ uv を使うので
    /// ST は 1 つで足りる（`Shaders/LitInput.hlsl`)。
    ///
    /// ■ **計器カメラは常時有効にしない。**
    /// 5 面ぶんのカメラを毎フレーム回す必要は無い。`enabled = false` のままにして、
    /// ここから 10 Hz で `Render()` を呼ぶ（`InstrumentPanel` の更新周期と同じ）。
    ///
    /// ■ **A / B は実機で見比べるための一時的な足場。**
    /// 面ごとに 2 つの Canvas（案 A 用・案 B 用）を組んであり、選ばれている側だけを
    /// 描く。決まったら選ばれなかった側ごと削除する。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitScreens : MonoBehaviour
    {
        /// <summary>更新周期。`InstrumentPanel` と同じ 10 Hz。</summary>
        public const double UpdateIntervalSeconds = InstrumentPanel.UpdateIntervalSeconds;

        [Serializable]
        public sealed class Screen
        {
            public string RendererName;
            public Renderer Target;
            public RenderTexture Texture;

            /// <summary>UV 矩形を 0..1 へ写す `_BaseMap_ST`。(scaleX, scaleY, offsetX, offsetY)</summary>
            public Vector4 BaseMapSt;

            /// <summary>案 A / B / C の描画元。**どれも enabled = false のまま使う。**</summary>
            public Camera CameraA;
            public Camera CameraB;
            public Camera CameraC;

            /// <summary>テスト柄の描画元 (Step 11-3b の切り分け道具)。</summary>
            public Camera CameraPattern;

            /// <summary>
            /// 視線に正対するクアッド (Step 11-3b)。**既定では隠してある。**
            /// 元の面の投影を覆う大きさ・位置で、組み立て時に置いてある。
            /// </summary>
            public Renderer Facing;

            /// <summary>
            /// 逆歪ませの出力先 (Step 11-3c)。**圧縮される軸に積んである**ので、
            /// `Texture` とは寸法が違う。中身が歪んでいるので比も面の実寸とは違う。
            /// </summary>
            public RenderTexture Warped;

            /// <summary>出力 (s,t) → 入力 (s,t) のホモグラフィ (Step 11-3c)。</summary>
            public Matrix4x4 Warp;

            /// <summary>blit に使うマテリアル。**面ごとに 1 枚**（行列が違う）。</summary>
            public Material WarpMaterial;

            /// <summary>マテリアルが Transparent か。**鏡面反射の扱いを分ける。**</summary>
            public bool Transparent;

            public Camera CameraFor(ScreenLayout layout)
            {
                switch (layout)
                {
                    case ScreenLayout.B: return CameraB;
                    case ScreenLayout.C: return CameraC;
                    default: return CameraA;
                }
            }
        }

        [SerializeField] List<Screen> _screens = new List<Screen>();
        [SerializeField] float _emission = (float)CockpitDefinition.DefaultScreenEmission;
        [SerializeField] ScreenLayout _layout = ScreenLayout.A;

        /// <summary>テスト柄を出しているか (Step 11-3b)。</summary>
        [SerializeField] bool _pattern;

        /// <summary>計器の見せ方 (Step 11-3c)。面に貼る / 正対 / 逆歪ませ。</summary>
        [SerializeField] ScreenMode _mode = ScreenMode.OnFace;

        /// <summary>blit のシェーダが読む行列の名前。</summary>
        public const string WarpProperty = "_Warp";

        MaterialPropertyBlock _block;
        double _nextUpdateAt;

        /// <summary>blit を実行した回数。**10 Hz に乗っているか**の検証用。</summary>
        public int WarpCount { get; private set; }

        /// <summary>`Render()` を呼んだ回数。10 Hz に落ちているかの検証用。</summary>
        public int RenderCount { get; private set; }

        public IReadOnlyList<Screen> Screens => _screens;

        public ScreenLayout Layout => _layout;

        public float Emission => _emission;

        public bool PatternEnabled => _pattern;

        public ScreenMode Mode => _mode;

        /// <summary>
        /// F4 から。**面に貼る / 正対 / 逆歪ませ を切り替える (Step 11-3c)。**
        ///
        /// 正対のときは元の面にも同じ RT を貼ったままにする（下に残る絵で
        /// クアッドの位置ずれが分かる）。逆歪ませは面に貼ったままなので、
        /// **クアッドは消して、面が読む RT を歪ませたほうへ差し替える。**
        /// </summary>
        public void SetMode(ScreenMode mode)
        {
            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            ApplyMode();
            ApplyMaterials();

            // 切り替えた瞬間に描き直す。次の 10 Hz を待つと古い絵が残る。
            _nextUpdateAt = 0.0;
        }

        void ApplyMode()
        {
            foreach (Screen screen in _screens)
            {
                if (screen?.Facing != null)
                {
                    screen.Facing.enabled = _mode == ScreenMode.Facing;
                }

                // **行列はマテリアルに保存されない**（.mat が持てるのは float /
                // color / vector / texture だけ）。実行時に必ず入れ直す。
                if (screen?.WarpMaterial != null)
                {
                    screen.WarpMaterial.SetMatrix(WarpProperty, screen.Warp);
                }

                // **貼る前に実体を作っておく。** 未作成の RenderTexture を
                // MaterialPropertyBlock へ入れると null として入り、面が
                // ベンダーの絵に戻ってしまう（PlayMode で実測）。
                if (screen?.Warped != null && !screen.Warped.IsCreated())
                {
                    screen.Warped.Create();
                }
            }
        }

        /// <summary>いま面が読むべき RT。逆歪ませのときだけ歪ませたほう。</summary>
        RenderTexture VisibleTexture(Screen screen)
        {
            return _mode == ScreenMode.Prewarp && screen.Warped != null
                ? screen.Warped
                : screen.Texture;
        }

        /// <summary>F4 から。**テスト柄と計器の表示を入れ替える。**</summary>
        public void SetPattern(bool on)
        {
            if (_pattern == on)
            {
                return;
            }

            _pattern = on;
            ApplyLayout();
            _nextUpdateAt = 0.0;
        }

        public void Bind(IEnumerable<Screen> screens)
        {
            _screens = new List<Screen>(screens);
        }

        void Start()
        {
            ApplyLayout();
            ApplyMode();
            ApplyMaterials();
        }

        /// <summary>10 Hz で描き直す。elapsedSeconds は Core の積算時間 (決定 D-24)。</summary>
        public void Tick(double elapsedSeconds)
        {
            if (elapsedSeconds < _nextUpdateAt)
            {
                return;
            }

            _nextUpdateAt = elapsedSeconds + UpdateIntervalSeconds;

            foreach (Screen screen in _screens)
            {
                Camera cam = _pattern ? screen?.CameraPattern : screen?.CameraFor(_layout);
                if (cam != null && screen.Texture != null)
                {
                    cam.Render();
                    RenderCount++;
                    Warp(screen);
                }
            }
        }

        /// <summary>F4 から。**選ばれた案の Canvas だけを描く。**</summary>
        public void SetLayout(ScreenLayout layout)
        {
            if (_layout == layout)
            {
                return;
            }

            _layout = layout;
            ApplyLayout();

            // 切り替えた瞬間に描き直す。次の 10 Hz を待つと古い絵が残る。
            _nextUpdateAt = 0.0;
        }

        /// <summary>F4 から。**アセットには書き戻さない**（MPB は実行時だけ）。</summary>
        public void SetEmission(float intensity)
        {
            if (Mathf.Approximately(_emission, intensity))
            {
                return;
            }

            _emission = intensity;
            ApplyMaterials();
        }

        /// <summary>選ばれていない側の Canvas を止める。**カメラは常に無効のまま。**</summary>
        void ApplyLayout()
        {
            foreach (Screen screen in _screens)
            {
                SetActive(screen?.CameraA, !_pattern && _layout == ScreenLayout.A);
                SetActive(screen?.CameraB, !_pattern && _layout == ScreenLayout.B);
                SetActive(screen?.CameraC, !_pattern && _layout == ScreenLayout.C);
                SetActive(screen?.CameraPattern, _pattern);
            }
        }

        static void SetActive(Camera cam, bool active)
        {
            if (cam == null)
            {
                return;
            }

            // カメラ自身は enabled = false のまま（描くのは Render() から）。
            // 描画元の Canvas を止めることで、選ばれていない側の更新を省く。
            cam.enabled = false;
            foreach (Canvas canvas in cam.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = active;
            }
        }

        /// <summary>
        /// 逆歪ませ (Step 11-3c)。**描いた直後に 1 回だけ blit する。**
        /// 毎フレームではなく、計器を描き直した 10 Hz に乗せる。
        /// </summary>
        void Warp(Screen screen)
        {
            if (_mode != ScreenMode.Prewarp
                || screen.Warped == null || screen.WarpMaterial == null)
            {
                return;
            }

            Graphics.Blit(screen.Texture, screen.Warped, screen.WarpMaterial);
            WarpCount++;
        }

        /// <summary>面ごとに RT と ST を差す。**マテリアルは共有のまま。**</summary>
        public void ApplyMaterials()
        {
            _block = _block ?? new MaterialPropertyBlock();

            foreach (Screen screen in _screens)
            {
                if (screen?.Target == null || screen.Texture == null)
                {
                    continue;
                }

                RenderTexture visible = VisibleTexture(screen);
                screen.Target.GetPropertyBlock(_block);
                _block.SetTexture("_BaseMap", visible);
                _block.SetTexture("_EmissionMap", visible);
                _block.SetVector("_BaseMap_ST", screen.BaseMapSt);
                _block.SetColor("_EmissionColor", new Color(_emission, _emission, _emission, 1f));

                // **透明な面の鏡面反射を落とす (Step 11-3b)。**
                // HUD のガラスは `_Smoothness 0.5` で、反射プローブが無い場でも
                // 環境の映り込みが出る。文字の無いところが**半透明の板**に見えていた
                // （実機で確認）。計画書 11-2d の「ガラスの反射は切る」と同じ扱い。
                if (screen.Transparent)
                {
                    _block.SetFloat("_Smoothness", 0f);
                    _block.SetFloat("_Metallic", 0f);
                }

                screen.Target.SetPropertyBlock(_block);

                // 正対クアッドは UV が 0..1 なので ST は要らない。
                if (screen.Facing != null)
                {
                    screen.Facing.GetPropertyBlock(_block);
                    _block.SetTexture("_BaseMap", visible);
                    _block.SetColor("_BaseColor", Color.white);
                    screen.Facing.SetPropertyBlock(_block);
                }
            }
        }
    }
}
