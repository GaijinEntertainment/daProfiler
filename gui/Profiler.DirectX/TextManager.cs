﻿using SharpDX;
using SharpDX.Direct3D11;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Xml;
using System.IO;
using System.Reflection;

namespace Profiler.DirectX
{
	public class TextManager : IDisposable
	{
		public enum VerticalDirection
		{
			CCW,
			CW
		};
		[StructLayout(LayoutKind.Sequential)]
		public struct Vertex
		{
			public Vector2 Position;
			public Vector2 UV;
			public SharpDX.Color Color;
		}

		DynamicBuffer<Vertex> VertexBuffer;
		DynamicBuffer<int> IndexBuffer;
		Mesh TextMesh;

		class Font : IDisposable
		{
			public struct Symbol
			{
				public RectangleF UV;
				public Size2F Size;
				public float Advance;
			}

			public Symbol[] Symbols = new Symbol[256];

			public Texture2D Texture;
			public ShaderResourceView TextureView;
			public double Size { get; set; }

			public static Font Create(Device device, String name)
			{
				Font font = new Font();

				Assembly assembly = Assembly.GetExecutingAssembly();

				using (Stream stream = assembly.GetManifestResourceStream(String.Format("Profiler.DirectX.Fonts.{0}.fnt", name)))
				{
					XmlDocument doc = new XmlDocument();
					doc.Load(stream);

					XmlNode desc = doc.SelectSingleNode("//info");
					font.Size = double.Parse(desc.Attributes["size"].Value);

					XmlNode info = doc.SelectSingleNode("//common");

					float width = float.Parse(info.Attributes["scaleW"].Value);
					float height = float.Parse(info.Attributes["scaleH"].Value);

					foreach (XmlNode node in doc.SelectNodes("//char"))
					{
						int id = int.Parse(node.Attributes["id"].Value);

						Symbol symbol = new Symbol();

						symbol.Size.Width = float.Parse(node.Attributes["width"].Value);
						symbol.Size.Height = float.Parse(node.Attributes["height"].Value);

						int x = int.Parse(node.Attributes["x"].Value);
						int y = int.Parse(node.Attributes["y"].Value);
						symbol.UV = new RectangleF(x / width, y / height, symbol.Size.Width / width, symbol.Size.Height / height);

						symbol.Advance = float.Parse(node.Attributes["xadvance"].Value);

						font.Symbols[id] = symbol;
					}

					using (Stream textureStream = assembly.GetManifestResourceStream(String.Format("Profiler.DirectX.Fonts.{0}_0.png", name)))
					{
						font.Texture = TextureLoader.CreateTex2DFromFile(device, textureStream);
					}

					font.TextureView = new ShaderResourceView(device, font.Texture);
				}

				return font;
			}

			public void Dispose()
			{
				Utilities.Dispose(ref Texture);
				Utilities.Dispose(ref TextureView);
			}
		}

		Font SegoeUI;

		public TextManager(DirectX.DirectXCanvas canvas)
		{
			double baseFontSize = 16.0;
			int desiredFontSize = (int)(RenderSettings.dpiScaleY * baseFontSize);

			int fontSize = 16;
			int[] sizes = { 16, 20, 24, 28, 32 };
			for (int i = 0; i < sizes.Length; ++i)
			{
				if (desiredFontSize < sizes[i])
					break;

				fontSize = sizes[i];
			}

			SegoeUI = Font.Create(canvas.RenderDevice, String.Format("SegoeUI_{0}_Normal", fontSize));
			VertexBuffer = new DynamicBuffer<Vertex>(canvas.RenderDevice, BindFlags.VertexBuffer);
			IndexBuffer = new DynamicBuffer<int>(canvas.RenderDevice, BindFlags.IndexBuffer);
			TextMesh = canvas.CreateMesh(DirectXCanvas.MeshType.Text);
			TextMesh.UseAlpha = true;
		}
		private byte[] ascii(String text)
        {
			System.Text.ASCIIEncoding ascii = new System.Text.ASCIIEncoding();
			return ascii.GetBytes(text);
		}

		public Size Measure(String text)
		{
			Size2F size = new Size2F(0, 0);
			foreach (byte c in ascii(text))
			{
				if (c >= SegoeUI.Symbols.Length)
					continue;
				Size2F symbolSize = SegoeUI.Symbols[c].Size;
				size.Width = size.Width + SegoeUI.Symbols[c].Advance;
				size.Height = Math.Max(size.Height, SegoeUI.Symbols[c].Size.Height);
			}
			return new Size(size.Width, size.Height);
		}

		public void Draw(System.Windows.Point pos, String text, System.Windows.Media.Color color, TextAlignment alignment = TextAlignment.Left, double maxWidth = double.MaxValue)
		{
			Color textColor = Utils.Convert(color);
			pos = new System.Windows.Point((int)pos.X, (int)pos.Y);

			byte[] str = ascii(text);

			switch (alignment)
			{
				case TextAlignment.Center:
					{
						double totalWidth = 0.0;
						for (int i = 0; i < str.Length; ++i)
							totalWidth += SegoeUI.Symbols[str[i]].Advance;

						double shift = Math.Max(0.0, (maxWidth - totalWidth) * 0.5);

						Vector2 origin = new Vector2((float)(pos.X + shift), (float)pos.Y);
						for (int i = 0; i < str.Length; ++i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];

							if (symbol.Size.Width > maxWidth)
								break;

							Draw(origin, symbol, textColor);
							origin.X += symbol.Advance;
							maxWidth -= symbol.Advance;
						}
					}
					break;

				case TextAlignment.Right:
					{
						Vector2 origin = new Vector2((float)(pos.X + maxWidth), (float)pos.Y);
						for (int i = str.Length - 1; i >= 0; --i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];
							origin.X -= symbol.Advance;

							if (symbol.Size.Width > maxWidth)
								break;

							Draw(origin, symbol, textColor);
							maxWidth -= symbol.Advance;
						}
					}
					break;

				default:
					{
						Vector2 origin = new Vector2((float)pos.X, (float)pos.Y);
						for (int i = 0; i < str.Length; ++i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];

							if (symbol.Size.Width > maxWidth)
								break;

							Draw(origin, symbol, textColor);
							origin.X += symbol.Advance;
							maxWidth -= symbol.Advance;
						}
					}
					break;
			}

		}

		public void DrawVertical(VerticalDirection dir, System.Windows.Point pos, String text, System.Windows.Media.Color color, TextAlignment alignment = TextAlignment.Left, double maxWidth = double.MaxValue)
		{
			Color textColor = Utils.Convert(color);
			pos = new System.Windows.Point((int)pos.X, (int)pos.Y);

			byte[] str = ascii(text);

			switch (alignment)
			{
				case TextAlignment.Center:
					{
						double totalWidth = 0.0;
						for (int i = 0; i < str.Length; ++i)
							totalWidth += SegoeUI.Symbols[str[i]].Advance;

						double shift = Math.Max(0.0, (maxWidth - totalWidth) * 0.5);

						Vector2 origin = new Vector2((float)(pos.X), (float)(pos.Y + shift));
						for (int i = 0; i < str.Length; ++i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];

							if (symbol.Size.Width > maxWidth)
								break;

							DrawVertical(dir, origin, symbol, textColor);
							origin.Y += dir == VerticalDirection.CCW ? +symbol.Advance : -symbol.Advance;
							maxWidth -= symbol.Advance;
						}
					}
					break;

				case TextAlignment.Right:
					{
						Vector2 origin = new Vector2((float)(pos.X), (float)(pos.Y + maxWidth));
						for (int i = str.Length - 1; i >= 0; --i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];
							origin.Y -= dir == VerticalDirection.CCW ? -symbol.Advance : +symbol.Advance;

							if (symbol.Size.Width > maxWidth)
								break;

							DrawVertical(dir, origin, symbol, textColor);
							maxWidth -= symbol.Advance;
						}
					}
					break;

				default:
					{
						Vector2 origin = new Vector2((float)pos.X, (float)pos.Y);
						for (int i = 0; i < str.Length; ++i)
						{
							Font.Symbol symbol = SegoeUI.Symbols[str[i]];

							if (symbol.Size.Width > maxWidth)
								break;

							DrawVertical(dir, origin, symbol, textColor);
							origin.Y -= dir == VerticalDirection.CCW ? +symbol.Advance : -symbol.Advance;
							maxWidth -= symbol.Advance;
						}
					}
					break;
			}

		}
		void DrawVertical(VerticalDirection dir, Vector2 pos, Font.Symbol symbol, Color color)
		{

			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X, pos.Y),
				UV = dir == VerticalDirection.CCW ? symbol.UV.TopLeft : symbol.UV.BottomRight,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X + symbol.Size.Height, pos.Y),
				UV = dir == VerticalDirection.CCW ? symbol.UV.BottomLeft : symbol.UV.TopRight,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X + symbol.Size.Height, pos.Y - symbol.Size.Width),
				UV = dir == VerticalDirection.CCW ? symbol.UV.BottomRight : symbol.UV.TopLeft,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X, pos.Y - symbol.Size.Width),
				UV = dir == VerticalDirection.CCW ? symbol.UV.TopRight : symbol.UV.BottomLeft,
				Color = color
			});
		}
		void Draw(Vector2 pos, Font.Symbol symbol, Color color)
		{

			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X, pos.Y),
				UV = symbol.UV.TopLeft,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X + symbol.Size.Width, pos.Y),
				UV = symbol.UV.TopRight,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X + symbol.Size.Width, pos.Y + symbol.Size.Height),
				UV = symbol.UV.BottomRight,
				Color = color
			});
			VertexBuffer.Add(new Vertex()
			{
				Position = new Vector2(pos.X, pos.Y + symbol.Size.Height),
				UV = symbol.UV.BottomLeft,
				Color = color
			});
		}

		public void Render(DirectX.DirectXCanvas canvas)
		{
			Freeze(canvas.RenderDevice);

			canvas.RenderDevice.ImmediateContext.PixelShader.SetSampler(0, canvas.TextSamplerState);
			canvas.RenderDevice.ImmediateContext.PixelShader.SetShaderResource(0, SegoeUI.TextureView);

			canvas.Draw(TextMesh);
		}

		public void Freeze(Device device)
		{
			TextMesh.PrimitiveCount = VertexBuffer.Count * 2 / 4;

			if (IndexBuffer.Count < TextMesh.PrimitiveCount * 6)
			{
				IndexBuffer.Capacity = TextMesh.PrimitiveCount * 6;

				while (IndexBuffer.Count < TextMesh.PrimitiveCount * 6)
				{
					int baseIndex = (IndexBuffer.Count * 4) / 6;
					foreach (int i in DynamicMesh.BoxTriIndices)
						IndexBuffer.Add(baseIndex + i);
				}

				IndexBuffer.Update(device);
			}

			VertexBuffer.Update(device);

			TextMesh.VertexBuffer = VertexBuffer.Buffer;
			TextMesh.IndexBuffer = IndexBuffer.Buffer;
			TextMesh.VertexBufferBinding = new VertexBufferBinding(TextMesh.VertexBuffer, Utilites.SizeOf<Vertex>(), 0);
		}

		public void Dispose()
		{
			SharpDX.Utilities.Dispose(ref SegoeUI);
			SharpDX.Utilities.Dispose(ref TextMesh);
		}
	}
}
