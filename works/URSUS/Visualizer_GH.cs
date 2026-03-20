#r "URSUS.dll"

// Grasshopper Script Component — IDWVisualizer wrapper
// URSUS.dll 참조 후 IDWVisualizer.Build()를 호출해 VisualizerResult를 GH 와이어로 출력한다.
//
// GH Script 컴포넌트 상단에 다음 참조 추가:
//   #r "URSUS.dll"
//
// 입력 파라미터:
//   boundary        Curve          SolverResult.UnionBoundary
//   centroids       List<Point3d>  SolverResult.Centroids
//   values          List<double>   SolverResult.AvgIncomes
//   resolution      double         Mesh 최대 엣지 길이 (기본 100.0)
//   power           double         IDW 지수 p (기본 3.0)
//   heightScale     double         Z 높이 배율 (기본 0.5)
//   heightRatio     double         Z 최대 높이 = bboxWidth × heightRatio (기본 0.25)
//   legendSteps     int            범례 단계 수 (기본 8)
//   colorStyle      int            0=Custom 1=BlueRed 2=Heatmap 3=Spectral 4=Viridis 5=Diverging 6=Grayscale
//   colorLow        Color          colorStyle=0 최솟값 색상
//   colorHigh       Color          colorStyle=0 최댓값 색상
//
// 출력 파라미터:
//   mesh            Mesh           Z 변위 + 컬러 메시
//   flatMesh        Mesh           Z=0 컬러 메시 (지도 오버레이용)
//   minVal          double         범례 최솟값
//   maxVal          double         범례 최댓값
//   legendMesh      Mesh           범례 그라디언트 바
//   legendDots      List<TextDot>  범례 값 레이블

#region Usings
using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using URSUS.Visualization;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    private void RunScript(
        Curve         boundary,
        List<Point3d> centroids,
        List<double>  values,
        double        resolution,
        double        power,
        double        heightScale,
        double        heightRatio,
        int           legendSteps,
        int           colorStyle,
        Color         colorLow,
        Color         colorHigh,
        ref object    mesh,
        ref object    flatMesh,
        ref object    minVal,
        ref object    maxVal,
        ref object    legendMesh,
        ref object    legendDots)
    {
        try
        {
            if (boundary == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "boundary가 null입니다."); return; }
            if (centroids == null || centroids.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "centroids가 비어 있습니다."); return; }
            if (values == null || values.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "values가 비어 있습니다."); return; }

            // 기본값 처리
            if (resolution   <= 0) resolution   = 100.0;
            if (power        <= 0) power        = 2.5;
            if (heightScale  <= 0) heightScale  = 0.5;
            if (heightRatio  <= 0) heightRatio  = 0.5;
            if (legendSteps  <= 1) legendSteps  = 8;
            if (colorLow.A   == 0) colorLow     = Color.FromArgb(44,  123, 182);
            if (colorHigh.A  == 0) colorHigh    = Color.FromArgb(215,  25,  28);

            var visualizer = new IDWVisualizer(
                centroids, values,
                resolution, power, heightScale,
                heightRatio, legendSteps, colorStyle,
                colorLow, colorHigh);

            VisualizerResult result = visualizer.Build(boundary);

            mesh       = result.Mesh;
            flatMesh   = result.FlatMesh;
            minVal     = result.MinVal;
            maxVal     = result.MaxVal;
            legendMesh = result.LegendMesh;
            legendDots = result.LegendDots;

            Print($"[Visualizer] 완료: Mesh Verts={result.Mesh.Vertices.Count}");
        }
        catch (Exception ex)
        {
            Print($"[ERR] {ex.GetType().Name}: {ex.Message}");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
