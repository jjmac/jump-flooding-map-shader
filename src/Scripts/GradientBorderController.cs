using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Linq;

public class GradientBorderController : MonoBehaviour
{
	[SerializeField] private Renderer imageMesh;
	[SerializeField] private GameObject labelPrefab;
	[SerializeField] private float mapScale = 100f;
	[SerializeField] public int MapWidth = 8192;
	[SerializeField] public int MapHeight = 8192;

	[Header("Image Data")]
	[SerializeField] public Texture2D ProvinceIdTex;
	[SerializeField] private Texture2D terrainNormalTex;
	[SerializeField] private Texture2D lookupTex;
	[SerializeField] private RenderTexture distanceTransformTex;
	[SerializeField] private Texture2D colorMapIndirectionTex;
	[SerializeField] private Texture2D displayedColorMapTex;
	[SerializeField] private Texture2D biomeColorMapTex;

	[Header("GPU")]
	[SerializeField] private ComputeShader distanceFieldCS;
	[SerializeField] private ComputeShader createIndirectionMapCS;
	[SerializeField] private ComputeShader getTerritoryPixelsCS;
	[SerializeField] private ComputeShader getProvinceEdgePixelsCS;
	[SerializeField] private ComputeShader getBorderBitmaskCS;

	[Header("Provinces")]
	private List<Province> ProvincesInImage;

	[HideInInspector] public Dictionary<Color32, Province> ProvinceColorDict;
	[HideInInspector] public Dictionary<int, Province> ProvinceIntIDDict;
	[HideInInspector] public Dictionary<int, Border> BorderIDDict;
	[HideInInspector] public List<Border> BorderList;

	private RenderTexture A;
	private RenderTexture B;

	private ComputeBuffer getTerritoryPixelsBuffer;
	private ComputeBuffer getTerritoryPixelsArgBuffer;

	private ComputeBuffer getEdgePixelsBuffer;
	private ComputeBuffer getEdgePixelsArgBuffer;

	private void Initialize()
	{
		BorderList = new List<Border>();

		//Create a render texture.
		A = new RenderTexture(ProvinceIdTex.width, ProvinceIdTex.height, 32);
		A.enableRandomWrite = true;
		A.filterMode = FilterMode.Point;
		A.wrapMode = TextureWrapMode.Repeat;
		A.Create();

		B = new RenderTexture(ProvinceIdTex.width, ProvinceIdTex.height, 32);
		B.enableRandomWrite = true;
		B.wrapMode = TextureWrapMode.Repeat;
		B.filterMode = FilterMode.Point;
		B.Create();

		distanceTransformTex = new RenderTexture(ProvinceIdTex.width, ProvinceIdTex.height, 32);
		distanceTransformTex.enableRandomWrite = true;
		distanceTransformTex.wrapMode = TextureWrapMode.Repeat;
		distanceTransformTex.filterMode = FilterMode.Bilinear;
		distanceTransformTex.Create();

		//Load province data
		if (ProvinceIdTex == null || lookupTex == null)
		{
			Debug.LogError("No province data or lookup map loaded in editor!");
			return;
		}

		MapWidth = ProvinceIdTex.width;
		MapHeight = ProvinceIdTex.height;

		getTerritoryPixelsBuffer = new ComputeBuffer(ProvinceIdTex.width * ProvinceIdTex.height, sizeof(float) * 2, ComputeBufferType.Append);
		getTerritoryPixelsBuffer.SetCounterValue(0);

		getTerritoryPixelsArgBuffer = new ComputeBuffer(4, sizeof(float) * 2, ComputeBufferType.IndirectArguments);

		getEdgePixelsBuffer = new ComputeBuffer(ProvinceIdTex.width * ProvinceIdTex.height, sizeof(float) * 2, ComputeBufferType.Append);
		getEdgePixelsBuffer.SetCounterValue(0);

		getEdgePixelsArgBuffer = new ComputeBuffer(4, sizeof(float) * 2, ComputeBufferType.IndirectArguments);

		CreateIndirectionTex();
	}

	private void CreateIndirectionTex()
	{
		RenderTexture temp = RenderTexture.GetTemporary(256, 256, 32);
		temp.enableRandomWrite = true;
		temp.wrapMode = TextureWrapMode.Clamp;
		temp.filterMode = FilterMode.Point;
		temp.Create();

		int createIndirectionCSKernel = createIndirectionMapCS.FindKernel("CSMain");
		createIndirectionMapCS.SetTexture(createIndirectionCSKernel, "ProvinceMap", ProvinceIdTex);
		createIndirectionMapCS.SetTexture(createIndirectionCSKernel, "LookupMap", lookupTex);
		createIndirectionMapCS.SetTexture(createIndirectionCSKernel, "Result", temp);
		createIndirectionMapCS.Dispatch(createIndirectionCSKernel, ProvinceIdTex.width / 32, ProvinceIdTex.height / 32, 1);
		GL.Flush();

		colorMapIndirectionTex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
		colorMapIndirectionTex.wrapMode = TextureWrapMode.Clamp;
		colorMapIndirectionTex.filterMode = FilterMode.Point;

		RenderTexture.active = temp;
		colorMapIndirectionTex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
		colorMapIndirectionTex.Apply();

		RenderTexture.active = null;
		RenderTexture.ReleaseTemporary(temp);
	}

	private void GenerateDisplayColorTex()
	{
		displayedColorMapTex = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
		displayedColorMapTex.filterMode = FilterMode.Point;

		int texWidth = displayedColorMapTex.width;
		int texHeight = displayedColorMapTex.height;

		NativeArray<byte> displayedColB = new NativeArray<byte>(displayedColorMapTex.height * (displayedColorMapTex.width * 4), Allocator.Temp);

		for (int y = 0; y < texHeight; y++)
		{
			for (int x = 0; x < texWidth; x++)
			{
				int pixelIndex = (x + (texWidth * y)) * 4;

				Color32 currentProvColorId = colorMapIndirectionTex.GetPixel(x, y);

				if (ProvinceColorDict.TryGetValue(currentProvColorId, out Province prov))
				{
					Color32 provCol = prov.ColorID;

					displayedColB[pixelIndex] = provCol.r;
					displayedColB[pixelIndex + 1] = provCol.g;
					displayedColB[pixelIndex + 2] = provCol.b;
					displayedColB[pixelIndex + 3] = 255;
				}
				else
				{
					displayedColB[pixelIndex] = 0;
					displayedColB[pixelIndex + 1] = 0;
					displayedColB[pixelIndex + 2] = 0;
					displayedColB[pixelIndex + 3] = 0;
				}
			}
		}

		displayedColorMapTex.SetPixelData(displayedColB, 0);
		displayedColorMapTex.Apply();

		displayedColB.Dispose();
	}

	private void GenerateDistanceTransformMap(Texture2D colorMap)
	{
		int initKernel = distanceFieldCS.FindKernel("Init");
		int jfaKernel = distanceFieldCS.FindKernel("JFA");
		int renderKernel = distanceFieldCS.FindKernel("CSRender");
		int blurKernel = distanceFieldCS.FindKernel("CSBlur");

		//Share it with the pixel shader.
		imageMesh.materials[1].SetTexture("_DistanceTransformMap", distanceTransformTex);

		//Provide the other textures and calculate the render texture with the JFA compute shader.
		distanceFieldCS.SetFloat("w", A.width);
		distanceFieldCS.SetFloat("h", A.height);
		distanceFieldCS.SetFloat("N", 16f); //Basically the max distance a pixel can be from an edge and still update.  22.6274f

		distanceFieldCS.SetTexture(initKernel, "Provinces", ProvinceIdTex);
		distanceFieldCS.SetTexture(initKernel, "Lookup", lookupTex);
		distanceFieldCS.SetTexture(initKernel, "Color", colorMap);
		distanceFieldCS.SetTexture(initKernel, "Out", B);

		distanceFieldCS.Dispatch(initKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 32
		distanceFieldCS.SetFloat("StepWidth", 32f);
		distanceFieldCS.SetTexture(jfaKernel, "In", B);
		distanceFieldCS.SetTexture(jfaKernel, "Out", A);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 16
		distanceFieldCS.SetFloat("StepWidth", 16f);
		distanceFieldCS.SetTexture(jfaKernel, "In", A);
		distanceFieldCS.SetTexture(jfaKernel, "Out", B);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 8
		distanceFieldCS.SetFloat("StepWidth", 8f);
		distanceFieldCS.SetTexture(jfaKernel, "In", B);
		distanceFieldCS.SetTexture(jfaKernel, "Out", A);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 4
		distanceFieldCS.SetFloat("StepWidth", 4f);
		distanceFieldCS.SetTexture(jfaKernel, "In", A);
		distanceFieldCS.SetTexture(jfaKernel, "Out", B);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 2
		distanceFieldCS.SetFloat("StepWidth", 2f);
		distanceFieldCS.SetTexture(jfaKernel, "In", B);
		distanceFieldCS.SetTexture(jfaKernel, "Out", A);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//JFA Offset 1
		distanceFieldCS.SetFloat("StepWidth", 1f); //
		distanceFieldCS.SetTexture(jfaKernel, "In", A);
		distanceFieldCS.SetTexture(jfaKernel, "Out", B);
		distanceFieldCS.Dispatch(jfaKernel, A.width / 32, A.height / 32, 1);

		//Render
		distanceFieldCS.SetTexture(renderKernel, "In", B);
		distanceFieldCS.SetTexture(renderKernel, "Out", A);
		distanceFieldCS.Dispatch(renderKernel, A.width / 32, A.height / 32, 1);

		//Blur
		distanceFieldCS.SetTexture(blurKernel, "In", A);
		distanceFieldCS.SetTexture(blurKernel, "Out", distanceTransformTex);
		distanceFieldCS.Dispatch(blurKernel, A.width / 32, A.height / 32, 1);
	}

	private float2[] GetTerritoryPixels(float[] _searchColFloat4, Texture2D _indirectionMap)
	{
		getTerritoryPixelsBuffer.SetCounterValue(0);
		getTerritoryPixelsCS.SetFloats("SearchColor", _searchColFloat4);
		getTerritoryPixelsCS.SetTexture(0, "Indirection", _indirectionMap);

		getTerritoryPixelsCS.Dispatch(0, ProvinceIdTex.width / 32, ProvinceIdTex.height / 32, 1);

		int[] args = new int[] { 0, 1, 0, 0 };
		getTerritoryPixelsArgBuffer.SetData(args);
		ComputeBuffer.CopyCount(getTerritoryPixelsBuffer, getTerritoryPixelsArgBuffer, 0);
		getTerritoryPixelsArgBuffer.GetData(args);

		float2[] result = new float2[args[0]];
		getTerritoryPixelsBuffer.GetData(result, 0, 0, args[0]);

		return result;
	}

	private (float[], float[]) GetTerritoryPixels(float[] _searchColFloat4, Texture2D _indirectionMap, bool floatTuple = false)
	{
		getTerritoryPixelsBuffer.SetCounterValue(0);
		getTerritoryPixelsCS.SetFloats("SearchColor", _searchColFloat4);
		getTerritoryPixelsCS.SetTexture(0, "Indirection", _indirectionMap);

		getTerritoryPixelsCS.Dispatch(0, ProvinceIdTex.width / 32, ProvinceIdTex.height / 32, 1);

		int[] args = new int[] { 0, 1, 0, 0 };
		getTerritoryPixelsArgBuffer.SetData(args);
		ComputeBuffer.CopyCount(getTerritoryPixelsBuffer, getTerritoryPixelsArgBuffer, 0);
		getTerritoryPixelsArgBuffer.GetData(args);

		float2[] result = new float2[args[0]];
		getTerritoryPixelsBuffer.GetData(result, 0, 0, args[0]);

		float[] resultX = result.AsParallel().Select((val) => val.x).ToArray();
		float[] resultY = result.AsParallel().Select((val) => val.y).ToArray();

		return (resultX, resultY);
	}

	private (float[], float[]) GetProvinceEdgePixels(float[] _searchColFloat4)
	{
		getEdgePixelsBuffer.SetCounterValue(0);
		getProvinceEdgePixelsCS.SetFloats("SearchColor", _searchColFloat4);
		getProvinceEdgePixelsCS.SetTexture(0, "ProvinceMap", ProvinceIdTex);

		getProvinceEdgePixelsCS.Dispatch(0, ProvinceIdTex.width / 32, ProvinceIdTex.height / 32, 1);

		int[] args = new int[] { 0, 1, 0, 0 };
		getEdgePixelsArgBuffer.SetData(args);
		ComputeBuffer.CopyCount(getEdgePixelsBuffer, getEdgePixelsArgBuffer, 0);
		getEdgePixelsArgBuffer.GetData(args);

		float2[] result = new float2[args[0]];
		getEdgePixelsBuffer.GetData(result, 0, 0, args[0]);

		float[] resultX = result.AsParallel().Select((val) => val.x).ToArray();
		float[] resultY = result.AsParallel().Select((val) => val.y).ToArray();

		return (resultX, resultY);
	}

	private void GetAllProvincePixels()
	{
		for (int i = 1; i < ProvincesInImage.Count; i++)
		{
			float[] searchCol = { ProvincesInImage[i].ColorID.r / 255f, ProvincesInImage[i].ColorID.g / 255f, ProvincesInImage[i].ColorID.b / 255f, 1f };
			(ProvincesInImage[i].PixelsX, ProvincesInImage[i].PixelsY) = GetTerritoryPixels(searchCol, displayedColorMapTex, true);
			ProvincesInImage[i].PixelSize = ProvincesInImage[i].PixelsX.Length;
			ProvincesInImage[i].MinXY = new float2(ProvincesInImage[i].PixelsX.AsParallel().Min(), ProvincesInImage[i].PixelsY.AsParallel().Min());
			ProvincesInImage[i].MaxXY = new float2(ProvincesInImage[i].PixelsX.AsParallel().Max(), ProvincesInImage[i].PixelsY.AsParallel().Max());

			ProvincesInImage[i].PixelsXInt = new int[ProvincesInImage[i].PixelsX.Length];
			ProvincesInImage[i].PixelsYInt = new int[ProvincesInImage[i].PixelsY.Length];

			int texWidth = ProvinceIdTex.width;

			ProvincesInImage[i].PixelsXInt = ProvincesInImage[i].PixelsX.AsParallel().Select((val) => Mathf.RoundToInt(val * texWidth)).ToArray();
			ProvincesInImage[i].PixelsYInt = ProvincesInImage[i].PixelsY.AsParallel().Select((val) => Mathf.RoundToInt(val * texWidth)).ToArray();
		}
	}

}
