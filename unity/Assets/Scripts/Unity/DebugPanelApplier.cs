using System.Collections.Generic;
using System.Text;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// デバッグパネルの状態を場面へ流し込む (Step 8-0b)。
    ///
    /// **アセットには書き戻さない。** 触るのは実行時のマテリアルインスタンスと
    /// コンポーネントだけ。
    ///
    /// Update() を持たない。UniverseRoot.Tick から呼ばれる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugPanelApplier : MonoBehaviour
    {
        [SerializeField] UniverseRoot _root;
        [SerializeField] CameraStackController _stack;
        [SerializeField] SunFlareController _flare;
        [SerializeField] CockpitShake _shake;
        [SerializeField] PostProcessPreset _post;
        [SerializeField] StationViewSet _stations;
        [SerializeField] Material _earthSurface;
        [SerializeField] Material _marsSurface;
        [SerializeField] Material _clouds;

        /// <summary>太陽の殻と光点 (Step 9-1)。**1 つの数値で両方に効かせる。**</summary>
        [SerializeField] Material _sunMesh;
        [SerializeField] Material _sunPoint;

        /// <summary>コロナ (Step 9-2)。太陽のみ。</summary>
        [SerializeField] Material _sunCorona;

        public void Bind(UniverseRoot root, CameraStackController stack, SunFlareController flare,
                         CockpitShake shake, PostProcessPreset post, StationViewSet stations,
                         Material earthSurface, Material marsSurface, Material clouds,
                         Material sunMesh, Material sunPoint, Material sunCorona)
        {
            _root = root;
            _stack = stack;
            _flare = flare;
            _shake = shake;
            _post = post;
            _stations = stations;
            _earthSurface = earthSurface;
            _marsSurface = marsSurface;
            _clouds = clouds;
            _sunMesh = sunMesh;
            _sunPoint = sunPoint;
            _sunCorona = sunCorona;
        }

        /// <summary>1 フレームぶん反映する。</summary>
        public void Apply(DebugPanelModel model)
        {
            if (model == null)
            {
                return;
            }

            ApplyTiers(model);
            ApplyBodies(model);
            ApplyNumbers(model);

            if (_post != null && _post.Volume != null)
            {
                _post.Volume.enabled = model.BoolOf("show.post");
            }

            if (_flare != null && _flare.Flare != null)
            {
                _flare.Flare.enabled = model.BoolOf("show.flare");
            }

            if (_stack != null && _stack.Deep != null)
            {
                _stack.Deep.clearFlags = model.BoolOf("show.skybox")
                    ? CameraClearFlags.Skybox
                    : CameraClearFlags.SolidColor;
            }

            if (_stations != null)
            {
                bool visible = model.BoolOf("show.stations");
                foreach (StationView view in _stations.Views)
                {
                    if (view == null)
                    {
                        continue;
                    }

                    foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = visible;
                    }
                }
            }
        }

        void ApplyTiers(DebugPanelModel model)
        {
            if (_stack == null)
            {
                return;
            }

            SetCamera(_stack.Deep, model.TierVisible(1, "tier.deep"));
            SetCamera(_stack.Near, model.TierVisible(2, "tier.near"));
            SetCamera(_stack.Nearfield, model.TierVisible(3, "tier.nearfield"));
            SetCamera(_stack.Cockpit, model.TierVisible(4, "tier.cockpit"));
        }

        static void SetCamera(Camera cam, bool on)
        {
            if (cam != null && cam.enabled != on)
            {
                cam.enabled = on;
            }
        }

        void ApplyBodies(DebugPanelModel model)
        {
            if (_root == null || _root.SolarSystem == null)
            {
                return;
            }

            bool clouds = model.BoolOf("show.clouds");

            foreach (CelestialBodyView view in _root.SolarSystem.Views)
            {
                if (view == null || view.Body == null)
                {
                    continue;
                }

                string name = view.Body.Name;
                SetRenderer(view.PointRenderer, model.BoolOf(DebugPanelModel.BodyId(name, "point")));
                SetRenderer(view.MeshRenderer, model.BoolOf(DebugPanelModel.BodyId(name, "proxy")));
                SetRenderer(view.RealMeshRenderer, model.BoolOf(DebugPanelModel.BodyId(name, "real")));

                bool proxyClouds = clouds && model.BoolOf(DebugPanelModel.BodyId(name, "proxy"));
                bool realClouds = clouds && model.BoolOf(DebugPanelModel.BodyId(name, "real"));
                SetRenderer(view.CloudRenderer, proxyClouds);
                SetRenderer(view.RealCloudRenderer, realClouds);
            }
        }

        static void SetRenderer(Renderer r, bool on)
        {
            if (r != null && r.enabled != on)
            {
                r.enabled = on;
            }
        }

        void ApplyNumbers(DebugPanelModel model)
        {
            float atmosphere = (float)model.NumberOf(DebugPanelModel.AtmosphereId);
            if (_earthSurface != null)
            {
                _earthSurface.SetFloat("_AtmosphereStrength", atmosphere);
            }

            if (_marsSurface != null)
            {
                _marsSurface.SetFloat("_AtmosphereStrength",
                    atmosphere * (float)PlanetAppearance.MarsAtmosphereRatio);
            }

            if (_clouds != null)
            {
                _clouds.SetFloat("_CloudOpacity", (float)model.NumberOf(DebugPanelModel.CloudId));
            }

            if (_flare != null)
            {
                _flare.SetBaseIntensity((float)model.NumberOf(DebugPanelModel.FlareId));

                // 見た目は実行時コピーへ。アセットには書き戻さない。
                _flare.Look.Apply(
                    model.NumberOf(DebugPanelModel.SpikeCountId),
                    model.NumberOf(DebugPanelModel.SpikeLengthId),
                    model.NumberOf(DebugPanelModel.SpikeThicknessId),
                    model.NumberOf(DebugPanelModel.GhostId));
            }

            if (_shake != null)
            {
                _shake.SetMaxAmplitude((float)model.NumberOf(DebugPanelModel.ShakeId));
            }

            // **殻と光点に同じ値を掛ける。** LOD 帯で不連続にしないため。
            var sun = (float)model.NumberOf(DebugPanelModel.SunEmissionId);
            if (_sunMesh != null) { _sunMesh.SetFloat("_EmissionIntensity", sun); }
            if (_sunPoint != null) { _sunPoint.SetFloat("_EmissionIntensity", sun); }

            // **コロナも本体と同じ強度。** 取り残されると縁で段差になる。
            if (_sunCorona != null)
            {
                _sunCorona.SetFloat("_EmissionIntensity", sun);
                _sunCorona.SetFloat("_Falloff",
                    (float)model.NumberOf(DebugPanelModel.CoronaFalloffId));
            }

            // **bloom は Volume のプロファイルへ直接。** 実行時コピーではないが、
            // PostProcessPreset.Awake が毎回 Core の定数から入れ直すので、
            // 次の起動には持ち越さない。
            if (_post != null && _post.Volume != null)
            {
                UnityEngine.Rendering.VolumeProfile profile =
                    _post.Volume.sharedProfile != null ? _post.Volume.sharedProfile : _post.Volume.profile;
                if (profile != null &&
                    profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
                {
                    bloom.active = true;
                    bloom.intensity.value = (float)model.NumberOf(DebugPanelModel.BloomIntensityId);
                    bloom.threshold.value = (float)model.NumberOf(DebugPanelModel.BloomThresholdId);
                    bloom.scatter.value = (float)model.NumberOf(DebugPanelModel.BloomScatterId);
                }
            }

            var coronaSize = (float)model.NumberOf(DebugPanelModel.CoronaSizeId);
            bool coronaOn = model.BoolOf("show.corona");
            if (_root != null && _root.SolarSystem != null)
            {
                foreach (CelestialBodyView v in _root.SolarSystem.Views)
                {
                    if (v == null) { continue; }

                    v.CoronaRadiusScale = coronaSize;
                    if (v.CoronaRenderer != null) { v.CoronaRenderer.enabled = coronaOn; }
                }
            }
        }
    }
}
