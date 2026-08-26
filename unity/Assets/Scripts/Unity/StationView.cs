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

            // ポートが深宇宙側を向くように据える。
            Vec3d port = Station.PortDirection;
            transform.localRotation = Quaternion.LookRotation(
                new Vector3((float)port.X, (float)port.Y, (float)port.Z));
        }
    }

    /// <summary>ステーションビューをまとめて更新する。</summary>
    [DisallowMultipleComponent]
    public sealed class StationViewSet : MonoBehaviour
    {
        [SerializeField] List<StationView> _views = new List<StationView>();

        public IReadOnlyList<StationView> Views => _views;

        public void Register(StationView view)
        {
            if (view != null && !_views.Contains(view))
            {
                _views.Add(view);
            }
        }

        public void Rebind(SolarSystemModel model)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null)
                {
                    _views[i].Rebind(model);
                }
            }
        }

        public void Apply(Vec3d observerAbsolute)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null)
                {
                    _views[i].Apply(observerAbsolute);
                }
            }
        }

        public StationView Find(string name)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null && _views[i].StationName == name)
                {
                    return _views[i];
                }
            }

            return null;
        }
    }
}
