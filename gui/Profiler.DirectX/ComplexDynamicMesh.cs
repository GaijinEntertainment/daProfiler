using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using System.Windows.Media;
using System.Windows;

namespace Profiler.DirectX
{
	public class ComplexDynamicMesh
	{
		List<DynamicMesh> DIPs = new List<DynamicMesh>();

		public ComplexDynamicMesh(DirectXCanvas canvas, int chunkCount = 20)
		{
			double scaleX = 1.0 / chunkCount;

			for (int i = 0; i < chunkCount; ++i)
			{
				DynamicMesh mesh = canvas.CreateMesh();
				mesh.LocalTransform = new Matrix(scaleX, 0.0, 0.0, 1.0, -i / chunkCount, 0.0);
				DIPs.Add(mesh);
			}
		}

		private DynamicMesh SelectMesh(Point p)
		{
			int index = Math.Min(DIPs.Count - 1, Math.Max((int)(p.X * DIPs.Count), 0));
			return DIPs[index];
		}

		public void AddRect(Rect rect, System.Windows.Media.Color color, uint id = 0u)
		{
			SelectMesh(rect.Location).AddRect(rect, color, id);
		}

		public void AddRect(System.Windows.Point[] rect, System.Windows.Media.Color color, uint id = 0u)
		{
			SelectMesh(rect[0]).AddRect(rect, color, id);
		}

		public void AddRect(Rect rect, System.Windows.Media.Color[] colors, uint id = 0u)
		{
			SelectMesh(rect.Location).AddRect(rect, colors, id);
		}

		public void AddTri(System.Windows.Point a, System.Windows.Point b, System.Windows.Point c, System.Windows.Media.Color color, uint id = 0u)
		{
			SelectMesh(a).AddTri(a, b, c, color, id);
		}

		public void AddLine(System.Windows.Point start, System.Windows.Point finish, System.Windows.Media.Color color, uint id = 0u)
		{
			SelectMesh(start).AddLine(start, finish, color, id);
		}

		public List<Mesh> Freeze(SharpDX.Direct3D11.Device device)
		{
			List<Mesh> result = new List<Mesh>(DIPs.Count);
			DIPs.ForEach(dip =>
			{
				Mesh mesh = dip.Freeze(device);
				if (mesh != null)
					result.Add(mesh);
			});
			return result;
		}
	}
}
