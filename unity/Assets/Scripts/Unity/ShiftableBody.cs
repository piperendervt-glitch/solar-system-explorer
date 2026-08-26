using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 絶対座標を持つルートオブジェクト (docs/01-architecture.md §2-3)。
    ///
    /// Transform.position は「原点相対座標の出力先」でしかなく、真の位置ではない。
    /// 真の位置は AbsolutePosition (double)。
    ///
    /// 付けるのは**ルートだけ**。子は親に付いて動くので登録しない
    /// (二重にシフトされて 2 倍動く)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShiftableBody : MonoBehaviour
    {
        [SerializeField] double _absoluteX;
        [SerializeField] double _absoluteY;
        [SerializeField] double _absoluteZ;

        public Vec3d AbsolutePosition
        {
            get => new Vec3d(_absoluteX, _absoluteY, _absoluteZ);
            set
            {
                _absoluteX = value.X;
                _absoluteY = value.Y;
                _absoluteZ = value.Z;
            }
        }

        /// <summary>原点相対座標を Transform へ書き出す。呼ぶのは OriginShiftDriver だけ。</summary>
        public void ApplyOrigin(FloatingOrigin origin)
        {
            Vec3d local = origin.ToOriginRelative(AbsolutePosition);
            transform.position = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        }
    }
}
