using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 天体ビューをまとめて更新する。Update() は持たず UniverseRoot から呼ばれる (決定 D-1)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SolarSystemView : MonoBehaviour
    {
        [SerializeField] List<CelestialBodyView> _views = new List<CelestialBodyView>();

        public IReadOnlyList<CelestialBodyView> Views => _views;

        public void Register(CelestialBodyView view)
        {
            if (view != null && !_views.Contains(view))
            {
                _views.Add(view);
            }
        }

        /// <summary>
        /// シーン読み込み後に全ビューを Core のモデルへ結び直す。
        /// CelestialBody は Core の素の C# クラスなので Unity がシリアライズできない。
        /// </summary>
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

        /// <summary>観測者の絶対位置から全天体を更新する。</summary>
        public void Apply(Vec3d observerAbsolute, double radiansPerPixel)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                CelestialBodyView view = _views[i];
                if (view != null)
                {
                    view.Apply(observerAbsolute, radiansPerPixel);
                }
            }
        }

        public CelestialBodyView Find(string bodyName)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null && _views[i].BodyName == bodyName)
                {
                    return _views[i];
                }
            }

            return null;
        }
    }
}
