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

        /// <summary>直近に実スケールへ引き渡した天体 (無ければ null)。</summary>
        public CelestialBodyView HandoffTarget { get; private set; }

        /// <summary>
        /// 実スケール引き渡しの有効/無効。false にすると Step 3a と同じ
        /// 「プロキシ殻だけ」の描画に戻る。前後比較のスクショ用。
        /// </summary>
        public bool HandoffEnabled { get; set; } = true;

        /// <summary>観測者の絶対位置から全天体を更新する。</summary>
        public void Apply(Vec3d observerAbsolute, double radiansPerPixel)
            => Apply(observerAbsolute, radiansPerPixel, 0.0);

        /// <summary>elapsedSeconds は自転角に使う (Step 8-4)。</summary>
        public void Apply(Vec3d observerAbsolute, double radiansPerPixel, double elapsedSeconds)
        {
            // 実スケールへ渡すのは**一番近い 1 天体だけ**、しかも帯の内側にいるときだけ。
            // 太陽 (2.28e8) と地球 (7.8e7) はこのシナリオでは決して 5e4 units に入らないので、
            // 常にプロキシ殻のまま残る。
            CelestialBodyView nearest = null;
            double nearestDistance = double.PositiveInfinity;

            for (int i = 0; i < _views.Count; i++)
            {
                CelestialBodyView view = _views[i];
                if (view == null || view.Body == null)
                {
                    continue;
                }

                double d = view.Body.DistanceFrom(observerAbsolute);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = view;
                }
            }

            HandoffTarget = (HandoffEnabled && RealScaleHandoff.IsActive(nearestDistance)) ? nearest : null;

            for (int i = 0; i < _views.Count; i++)
            {
                CelestialBodyView view = _views[i];
                if (view != null)
                {
                    view.SetHandoffTarget(ReferenceEquals(view, HandoffTarget));
                    view.Apply(observerAbsolute, radiansPerPixel, elapsedSeconds);
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
