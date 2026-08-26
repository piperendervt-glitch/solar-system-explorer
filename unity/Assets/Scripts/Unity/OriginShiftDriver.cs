using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// FloatingOrigin の結果を全 ShiftableBody に配る (docs/01-architecture.md §2-3)。
    ///
    /// Update() を持たない。呼ぶのは UniverseRoot だけ (決定 D-1)。
    /// 各 View が勝手に Update() で Transform をいじると、再基準化との順序が壊れて
    /// 1 フレームぶんズレる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OriginShiftDriver : MonoBehaviour
    {
        readonly List<ShiftableBody> _bodies = new List<ShiftableBody>();

        public IReadOnlyList<ShiftableBody> Bodies => _bodies;

        public void Register(ShiftableBody body)
        {
            if (body != null && !_bodies.Contains(body))
            {
                _bodies.Add(body);
            }
        }

        public void Unregister(ShiftableBody body) => _bodies.Remove(body);

        /// <summary>
        /// シーン内の ShiftableBody をすべて集め直す。
        /// シーンは Editor スクリプトから毎回生成する (決定 D-20) ので、
        /// 起動時に 1 回集めれば足りる。
        /// </summary>
        public void CollectFromScene()
        {
            _bodies.Clear();
#if UNITY_2022_2_OR_NEWER
            ShiftableBody[] found = Object.FindObjectsByType<ShiftableBody>(FindObjectsSortMode.None);
#else
            ShiftableBody[] found = Object.FindObjectsOfType<ShiftableBody>();
#endif
            _bodies.AddRange(found);
        }

        /// <summary>登録済みの全 ShiftableBody に原点相対座標を書き出す。</summary>
        public void Apply(FloatingOrigin origin)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                ShiftableBody body = _bodies[i];
                if (body != null)
                {
                    body.ApplyOrigin(origin);
                }
            }
        }
    }
}
