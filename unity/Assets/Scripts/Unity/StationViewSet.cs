using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
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
