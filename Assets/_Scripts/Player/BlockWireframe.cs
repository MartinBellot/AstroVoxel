// ============================================================
//  BlockWireframe.cs
//  Dessine un wireframe 1×1×1 en GL à la position du Transform.
//  Utilise Hidden/Internal-Colored — fonctionne avec tous les
//  pipelines Unity (Built-in, URP, HDRP).
// ============================================================

using UnityEngine;

namespace AstroVoxel.Player
{
    public sealed class BlockWireframe : MonoBehaviour
    {
        // Légèrement plus grand qu'un bloc pour éviter le z-fighting
        private const float Offset =  -0.002f;
        private const float Size   =   1.004f;

        private static Material _mat;

        private static Material GetMat()
        {
            if (_mat != null) return _mat;

            var shader = Shader.Find("Hidden/Internal-Colored");
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull",     0);
            _mat.SetInt("_ZWrite",   1);
            _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            return _mat;
        }

        private void OnRenderObject()
        {
            if (!gameObject.activeInHierarchy) return;

            GetMat().SetPass(0);

            Vector3    p = transform.position;
            Quaternion r = transform.rotation;   // suit l'orientation du chunk (face planète)
            float      o = Offset;
            float      s = Size;

            // 8 coins du cube — offsets exprimés dans l'espace LOCAL du bloc,
            // puis tournés via la rotation du transform pour s'aligner avec
            // la face de la planète sur laquelle se trouve le chunk.
            var v = new Vector3[8]
            {
                p + r * new Vector3(o, o, o),   // 0
                p + r * new Vector3(s, o, o),   // 1
                p + r * new Vector3(o, s, o),   // 2
                p + r * new Vector3(s, s, o),   // 3
                p + r * new Vector3(o, o, s),   // 4
                p + r * new Vector3(s, o, s),   // 5
                p + r * new Vector3(o, s, s),   // 6
                p + r * new Vector3(s, s, s),   // 7
            };

            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(Color.black);

            // 4 arêtes en X
            Seg(v[0], v[1]); Seg(v[2], v[3]); Seg(v[4], v[5]); Seg(v[6], v[7]);
            // 4 arêtes en Y
            Seg(v[0], v[2]); Seg(v[1], v[3]); Seg(v[4], v[6]); Seg(v[5], v[7]);
            // 4 arêtes en Z
            Seg(v[0], v[4]); Seg(v[1], v[5]); Seg(v[2], v[6]); Seg(v[3], v[7]);

            GL.End();
            GL.PopMatrix();
        }

        private static void Seg(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }
    }
}
