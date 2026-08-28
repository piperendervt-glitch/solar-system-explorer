using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;
using UnityEngine.XR;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **XR 実行時の f と IPD を測る (Step 12-1b)。値の取得だけ。判定はしない。**
    ///
    /// `StereoGeometry` の入力（焦点距離 f と眼間距離）は、平面のときの
    /// 見積もり（1080p / 60 度で 935.31 px / IPD 63 mm）をそのまま使えない:
    ///
    ///   - **XR の画角はカメラの `fieldOfView` ではなく HMD の投影行列が決める**
    ///   - 目テクスチャは 12-0d の実測で 1512x1680（1080p ではない）
    ///   - MockHMD や実機が実際に何 mm の眼間距離を使うかは別問題
    ///
    /// **見積もりのまま「実測 / 理論」を取ると、描画が正しくても比が 1.0 から外れる。**
    /// ここで実行時の値を採り、それを理論値の入力にする。
    ///
    /// ■ 単位に注意
    /// 眼間距離は**そのカメラのワールド単位**で出る。コックピット段は
    /// 1 m = 1 unit、外 3 段は 1 m = 0.001 units（1 unit = 1 km）なので、
    /// **同じ数字でも段によって意味が違う。** `StageScale` を併記する。
    ///
    /// ■ 読む時機
    /// 12-0d と同じ。**初期化直後の値は当てにならない。**
    /// `XrFactsLogger` が frame 1 / 2 / 10 / 60 / 120 で読み直す。
    /// </summary>
    public static class XrStereoOptics
    {
        /// <summary>1 台のカメラから読んだ値。**判定はしない。**</summary>
        public sealed class Reading
        {
            public string CameraName = string.Empty;

            /// <summary>この段の m -> units の換算。コックピット段 1.0 / 外 3 段 0.001。</summary>
            public double StageScale;

            /// <summary>ステレオとして描かれているか。false なら以下の値は当てにならない。</summary>
            public bool StereoEnabled;

            /// <summary>投影行列の m11 = 1/tan(縦画角/2)。左右で違いうる。</summary>
            public double M11Left;
            public double M11Right;

            /// <summary>
            /// 横方向の倍率 m00 と、**主点のずれ** m02 / m12 (Step 12-2b)。
            ///
            /// XR の錐台は非対称で、m02 / m12 が 0 でない。**これが左右の絵を
            /// 丸ごとずらす**ので、画素座標のまま左右を比べると視差ではなく
            /// 主点のずれを測ってしまう（12-2 で 4 段とも -42〜-72 px が出た）。
            /// 判定は角度空間で行うこと。
            /// </summary>
            public double M00Left;
            public double M00Right;
            public double M02Left;
            public double M02Right;
            public double M12Left;
            public double M12Right;

            /// <summary>目テクスチャの高さ [px]。f の換算に要る。</summary>
            public int EyeTextureHeight;

            /// <summary>f [px] = (H/2) * m11。**目ごとに出す。**</summary>
            public double FocalLengthPixelsLeft;
            public double FocalLengthPixelsRight;

            /// <summary>平面のときの縦画角 [deg]（XR では投影行列に上書きされる）。</summary>
            public double CameraFieldOfView;

            /// <summary>左右の目の間隔。**このカメラのワールド単位。**</summary>
            public double EyeSeparationUnits;

            /// <summary>上を m に直した値（= 間隔 / StageScale）。</summary>
            public double EyeSeparationMeters;

            /// <summary>カメラのワールドスケール。**EyeAnchor が効いているかの確認 (12-2)。**</summary>
            public double LossyScale;

            /// <summary>左右の目のワールド位置。**段ごとに違うかを見る (12-2)。**</summary>
            public Vector3 EyeLeftWorld;
            public Vector3 EyeRightWorld;

            public override string ToString()
                => $"{CameraName} (s={StageScale}) / stereo={StereoEnabled}"
                   + $" / eyeH={EyeTextureHeight}"
                   + $" / m11 L={M11Left:F6} R={M11Right:F6}"
                   + $" / m00 L={M00Left:F6} R={M00Right:F6}"
                   + $" / m02 L={M02Left:F6} R={M02Right:F6}"
                   + $" / m12 L={M12Left:F6} R={M12Right:F6}"
                   + $" / f L={FocalLengthPixelsLeft:F4} R={FocalLengthPixelsRight:F4} px"
                   + $" / fov={CameraFieldOfView:F2} 度"
                   + $" / lossyScale={LossyScale:E3}"
                   + $" / 眼間={EyeSeparationUnits:E6} units = {EyeSeparationMeters:E6} m"
                   + $" / 目L={EyeLeftWorld:F6} 目R={EyeRightWorld:F6}";
        }

        /// <summary>
        /// カメラ 1 台から読む。**XR が動いていなければ値は入らない**
        /// （`StereoEnabled = false` を返し、嘘の数字を並べない）。
        /// </summary>
        public static Reading Read(Camera camera, string name, double stageScale)
        {
            var reading = new Reading
            {
                CameraName = name,
                StageScale = stageScale,
                EyeTextureHeight = XRSettings.eyeTextureDesc.height,
            };

            if (camera == null)
            {
                reading.CameraName = name + " (無し)";
                return reading;
            }

            reading.CameraFieldOfView = camera.fieldOfView;
            reading.LossyScale = camera.transform.lossyScale.x;
            reading.StereoEnabled = camera.stereoEnabled;

            if (!reading.StereoEnabled)
            {
                return reading;
            }

            Matrix4x4 projectionLeft = camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 projectionRight = camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

            reading.M11Left = projectionLeft.m11;
            reading.M11Right = projectionRight.m11;
            reading.M00Left = projectionLeft.m00;
            reading.M00Right = projectionRight.m00;
            reading.M02Left = projectionLeft.m02;
            reading.M02Right = projectionRight.m02;
            reading.M12Left = projectionLeft.m12;
            reading.M12Right = projectionRight.m12;

            if (reading.M11Left > 0.0 && reading.EyeTextureHeight > 0)
            {
                reading.FocalLengthPixelsLeft = StereoGeometry.FocalLengthPixelsFromProjection(
                    reading.M11Left, reading.EyeTextureHeight);
            }

            if (reading.M11Right > 0.0 && reading.EyeTextureHeight > 0)
            {
                reading.FocalLengthPixelsRight = StereoGeometry.FocalLengthPixelsFromProjection(
                    reading.M11Right, reading.EyeTextureHeight);
            }

            // ビュー行列はワールド -> 目。逆行列の 4 列目が目のワールド位置。
            Vector3 eyeLeft = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left)
                .inverse.GetColumn(3);
            Vector3 eyeRight = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right)
                .inverse.GetColumn(3);

            reading.EyeLeftWorld = eyeLeft;
            reading.EyeRightWorld = eyeRight;
            reading.EyeSeparationUnits = (eyeRight - eyeLeft).magnitude;
            reading.EyeSeparationMeters = stageScale > 0.0
                ? reading.EyeSeparationUnits / stageScale
                : 0.0;

            return reading;
        }

        /// <summary>
        /// カメラ 4 段すべてから読む。**段ごとにスケールが違う**ので、
        /// どの段の値かを取り違えないよう名前とスケールを付ける。
        /// </summary>
        public static List<Reading> ReadStack(CameraStackController stack)
        {
            var readings = new List<Reading>();
            if (stack == null)
            {
                return readings;
            }

            readings.Add(Read(stack.Deep, "Deep", StereoGeometry.WorldStageScale));
            readings.Add(Read(stack.Near, "Near", StereoGeometry.WorldStageScale));
            readings.Add(Read(stack.Nearfield, "Nearfield", StereoGeometry.WorldStageScale));
            readings.Add(Read(stack.Cockpit, "Cockpit", StereoGeometry.CockpitStageScale));
            return readings;
        }
    }
}
