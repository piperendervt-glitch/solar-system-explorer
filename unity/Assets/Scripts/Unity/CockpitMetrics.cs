using System.Collections.Generic;
using System.Text;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// 窓が視界のどれだけを占めるかを測る (Step 11-2b)。
    ///
    /// ■ **これは目の位置を決めるための物差し。**
    /// 「窓が広いか」は最後は目で決めるが、決めた結果を数値で残せないと、
    /// **別のコックピットに替えたときに比べられない。** 11-5 の購入判断では
    /// 無料サンプルと有料アセットをこの比で比べる。
    ///
    /// ■ 観測経路
    /// `Start` で 1 回ログに出す（**batchmode の PlayMode テストでも読める**）。
    /// F4 のデバッグパネルにも同じ値を出す（実機で目の位置を振りながら見るため）。
    /// パネルは OnGUI なので RenderTexture には写らない（CLAUDE.md §0-B）。
    ///
    /// ■ 近似
    /// ガラスのメッシュ頂点を投影し、**凸包の面積**を画面面積で割る。
    /// 凹んだシルエットでは実際より大きく出る。`ProjectedAreaSolver` の注記を参照。
    /// </summary>
    public sealed class CockpitMetrics : MonoBehaviour
    {
        /// <summary>窓と見なすマテリアル名の一部。実測: `Cockpit3Grey_Glass` ほか。</summary>
        public const string GlassMaterialKeyword = "Glass";

        /// <summary>投影する頂点の上限。**F4 パネルの天体行と同じ考え方で間引く。**</summary>
        public const int MaxSampledVertices = 512;

        /// <summary>
        /// **測定条件（固定）。** 面積比はアスペクト比で変わるので、実際の
        /// ウィンドウの大きさでは測らない。
        ///
        /// ■ なぜ固定するか（実機で踏んだ）
        /// 同じ目の位置でも 640x480 (4:3) では 10.4 %、1920x1080 (16:9) では 7.8 %。
        /// 垂直画角が同じなら**横に広い画面ほど横方向の視野が広く**、
        /// 窓の占める割合は小さく出る。**どちらも「その条件での正しい値」**だが、
        /// これでは 11-5 で無料と有料のコックピットを比べる物差しにならない。
        /// Demo 2 の `UniverseConstants.RadiansPerPixel` を 1080p 固定にしたのと同じ理由。
        ///
        /// **画角は固定しない。** 画角は F4 で人が決める値で、決めた画角での
        /// 見え方こそが知りたいものだから。**代わりにログへ併記する。**
        /// </summary>
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;

        [SerializeField] Camera _camera;
        [SerializeField] List<Renderer> _glass = new List<Renderer>();

        /// <summary>直近に測った比。窓が無い / 測れないときは負。</summary>
        public double LastRatio { get; private set; } = -1.0;

        public IReadOnlyList<Renderer> Glass => _glass;

        public void Bind(Camera cockpitCamera, IEnumerable<Renderer> glassRenderers)
        {
            _camera = cockpitCamera;
            _glass = new List<Renderer>(glassRenderers);
        }

        /// <summary>
        /// コックピットの寸法（この GameObject を基準にした AABB）。
        /// **F4 で目を振れる範囲**に使う。座席の位置が未知なので、
        /// 機体の寸法いっぱいを振れないと目的の場所へ届かない。
        ///
        /// **計器の描画元は除く。** `InstrumentSource` は他のカメラに写らないよう
        /// 視界の外（y = 1e5）に置いてあるので、含めると寸法が桁で狂う。
        /// </summary>
        public Bounds LocalBounds()
        {
            Bounds? combined = null;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnderInstrumentSource(renderer.transform))
                {
                    continue;
                }

                Vector3 center = transform.InverseTransformPoint(renderer.bounds.center);
                var b = new Bounds(center, renderer.bounds.size);
                if (combined.HasValue)
                {
                    Bounds acc = combined.Value;
                    acc.Encapsulate(b);
                    combined = acc;
                }
                else
                {
                    combined = b;
                }
            }

            return combined ?? new Bounds(Vector3.zero, Vector3.one);
        }

        static bool IsUnderInstrumentSource(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name == "InstrumentSource")
                {
                    return true;
                }
            }

            return false;
        }

        void Start()
        {
            Debug.Log("[CockpitMetrics] " + Describe());
        }

        /// <summary>
        /// 窓の投影面積比を測る。**測れないときは負を返す**（嘘の数字を出さない）。
        /// 隅がカメラの後ろにあるときも測れない扱いにする（投影が破綻するため）。
        /// </summary>
        public double Measure(out int sampledVertices, out int rendererCount,
                              out int behindCamera)
        {
            sampledVertices = 0;
            rendererCount = 0;
            behindCamera = 0;

            if (_camera == null || _glass == null || _glass.Count == 0)
            {
                LastRatio = -1.0;
                return LastRatio;
            }

            var xs = new List<double>();
            var ys = new List<double>();

            foreach (Renderer renderer in _glass)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                Mesh mesh = MeshOf(renderer);
                if (mesh == null)
                {
                    continue;
                }

                rendererCount++;
                Vector3[] vertices = mesh.vertices;
                int stride = Mathf.Max(1, vertices.Length / MaxSampledVertices);

                for (int i = 0; i < vertices.Length; i += stride)
                {
                    Vector3 world = renderer.transform.TransformPoint(vertices[i]);
                    Vector3 screen = ProjectToReference(world);

                    if (screen.z <= 0f)
                    {
                        // **カメラの後ろの点は投影が破綻する**ので除外する。
                        // キャノピーは目の位置より後ろにも回り込むので、除外は普通に起きる。
                        // 何点落としたかを数えて出す（黙って捨てない）。
                        behindCamera++;
                        continue;
                    }

                    xs.Add(screen.x);
                    ys.Add(screen.y);
                    sampledVertices++;
                }
            }

            if (xs.Count < 3)
            {
                LastRatio = -1.0;
                return LastRatio;
            }

            double area = ProjectedAreaSolver.ConvexHullArea(xs, ys);
            LastRatio = ProjectedAreaSolver.ScreenRatio(area, ReferenceWidth, ReferenceHeight);
            return LastRatio;
        }

        /// <summary>
        /// **基準の画面（1920x1080）へ投影する。** 実際のウィンドウの大きさは見ない。
        ///
        /// `Camera.WorldToScreenPoint` は実際のアスペクト比を使うので、
        /// ウィンドウの形で答えが変わってしまう。ここでは**カメラの位置・姿勢・画角だけ**
        /// を使い、アスペクトは基準値に固定して自前で射影する。
        /// 戻り値の z は視線方向の距離（負なら後ろ）。
        /// </summary>
        Vector3 ProjectToReference(Vector3 world)
        {
            Vector3 view = _camera.transform.InverseTransformPoint(world);
            if (view.z <= 0f)
            {
                return new Vector3(0f, 0f, view.z);
            }

            const float aspect = ReferenceWidth / (float)ReferenceHeight;
            float tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float ndcY = view.y / (view.z * tanHalf);
            float ndcX = view.x / (view.z * tanHalf * aspect);

            return new Vector3((ndcX * 0.5f + 0.5f) * ReferenceWidth,
                               (ndcY * 0.5f + 0.5f) * ReferenceHeight,
                               view.z);
        }

        /// <summary>ログと F4 に出す 1 行。</summary>
        public string Describe()
        {
            double ratio = Measure(out int sampled, out int renderers, out int behind);

            var sb = new StringBuilder("窓の投影面積比（凸包近似）: ");
            if (ratio < 0.0)
            {
                sb.Append(_glass == null || _glass.Count == 0
                    ? "--- （窓のレンダラーが無い。箱コックピットには窓が無い）"
                    : "--- （カメラの後ろに頂点があるため測れない）");
                return sb.ToString();
            }

            sb.Append($"{ratio * 100.0:F1} %  ");
            sb.Append($"(レンダラー {renderers} / 投影した頂点 {sampled} / ");
            sb.Append(behind > 0 ? $"後方のため除外 {behind} / " : string.Empty);

            // **測定条件を必ず併記する。** 数字だけでは比べられない。
            float fov = _camera != null ? _camera.fieldOfView : 0f;
            sb.Append($"基準 {ReferenceWidth}x{ReferenceHeight} / 画角 {fov:F1} 度)");
            return sb.ToString();
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
