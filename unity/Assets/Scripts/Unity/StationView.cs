using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// ステーション 1 基の見た目 (Step 5)。
    ///
    /// 実スケールで Nearfield 段 (near 0.01 / far 100) が描く。
    /// 観測者は常に原点なので、原点相対座標をそのまま Transform に書く。
    ///
    /// Update() を持たない。呼ぶのは StationViewSet (さらに上は UniverseRoot) だけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StationView : MonoBehaviour
    {
        // SpaceStation は Core の素の C# クラスなので Unity はシリアライズできない。
        // 名前だけ残して起動時に引き直す (CelestialBodyView と同じ)。
        [SerializeField] string _stationName;

        public SpaceStation Station { get; private set; }

        public string StationName => _stationName;

        /// <summary>直近の距離 [units]。</summary>
        public double LastDistance { get; private set; }

        public void Bind(SpaceStation station)
        {
            Station = station;
            _stationName = station != null ? station.Name : null;
        }

        public void Rebind(SolarSystemModel model)
        {
            if (model == null || string.IsNullOrEmpty(_stationName))
            {
                return;
            }

            for (int i = 0; i < model.Stations.Count; i++)
            {
                if (model.Stations[i].Name == _stationName)
                {
                    Station = model.Stations[i];
                    return;
                }
            }

            Debug.LogWarning($"[StationView] モデルに '{_stationName}' が無い。");
        }

        int _frames;

        public void Apply(Vec3d observerAbsolute)
        {
            if (Station == null)
            {
                return;
            }

            Vec3d local = Station.AbsolutePosition - observerAbsolute;
            LastDistance = local.Magnitude;

            // Nearfield 段の far は 100 units。それより遠いと描いても意味がないので隠す。
            bool visible = LastDistance < CameraStackController.NearfieldFarClip;
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            transform.localPosition = new Vector3((float)local.X, (float)local.Y, (float)local.Z);

            // **姿勢は Core の基底から作る (Step 13-3 コミット2)。**
            // 判定側（`SpaceStation.PortPosition`）と同じものを読ませる。
            // `Quaternion.LookRotation(PortDirection)` と同じ基底であることは
            // `StationBasisTests` が突き合わせて縛っている。
            StationBasis basis = Station.Basis;
            transform.localRotation = Quaternion.LookRotation(ToUnity(basis.Forward),
                                                              ToUnity(basis.Up));

            // **倍率も定義から読む (Step 13-3 コミット2)。**
            // 形は `ModelRadius`（プレハブ単位）で組まれており、ここで units へ写す。
            // **半径・ポート位置・MinStandoff も同じ `EffectiveScale` から出る**ので、
            // 絵と数字が別のものを見ることはない。
            float scale = (float)Station.Definition.EffectiveScale;
            transform.localScale = new Vector3(scale, scale, scale);

            // **最初の 1 回だけ実体を数で残す (Step 13-3b の切り分け)。**
            // 「置いたつもり」と「そこに在って描かれる」は別。
            // **フレームが進んでから測る。** 初回は視錐台の判定がまだ更新されていない。
            _frames++;
            if (_frames == 200)
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                int shown = 0;
                foreach (Renderer r in renderers)
                {
                    if (r.isVisible) { shown++; }
                }

                Debug.Log($"[StationView] {name} / def={Station.Definition.Id}"
                          + $" / scale={scale:F5} / 距離={LastDistance:F5} units"
                          + $" / pos={transform.localPosition:F5}"
                          + $" / renderers={renderers.Length} 見えている={shown}"
                          + $" / layer={gameObject.layer}");

                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    foreach (Renderer r in renderers) { b.Encapsulate(r.bounds); }
                    Debug.Log($"[StationView]   ワールド bbox 中心 {b.center:F5} / サイズ {b.size:F5}");
                }

                Camera cam = Camera.main;
                if (cam != null)
                {
                    Debug.Log($"[StationView]   main cam pos={cam.transform.position:F5}"
                              + $" fwd={cam.transform.forward:F5} mask={cam.cullingMask}");
                }
            }
        }

        static Vector3 ToUnity(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
