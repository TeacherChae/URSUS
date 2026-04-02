using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using URSUS.Resources;
using URSUS.Visualization;

namespace URSUS.GH
{
    public class VisualizerComponent : GH_Component
    {
        public VisualizerComponent()
            : base(
                "URSUS Visualizer", "Viz",
                "IDW 공간 보간 시각화",
                "URSUS", "Visualization")
        { }

        public override Guid ComponentGuid
            => new Guid("392dfc85-c773-4489-938a-188fb90acb50");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B",
                "전체 외곽선 (SolverResult.UnionBoundary)", GH_ParamAccess.item);
            pManager.AddPointParameter("Centroids", "C",
                "법정동 중심점", GH_ParamAccess.list);
            pManager.AddNumberParameter("Values", "V",
                "보간할 값 (SolverResult.Values)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Resolution", "R",
                "메시 최대 엣지 길이", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Power", "P",
                "IDW 지수", GH_ParamAccess.item, 2.5);
            pManager.AddNumberParameter("Height Scale", "HS",
                "Z 높이 배율", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Height Ratio", "HR",
                "Z 최대 높이 = bboxWidth × HR", GH_ParamAccess.item, 0.5);
            pManager.AddIntegerParameter("Legend Steps", "LS",
                "범례 단계 수", GH_ParamAccess.item, 8);
            pManager.AddIntegerParameter("Color Style", "CS",
                "0=Custom 1=BlueRed 2=Heatmap 3=Spectral 4=Viridis 5=Diverging 6=Grayscale",
                GH_ParamAccess.item, 4);
            pManager.AddColourParameter("Color Low", "CL",
                "colorStyle=0일 때 최솟값 색상",
                GH_ParamAccess.item, Color.FromArgb(44, 123, 182));
            pManager.AddColourParameter("Color High", "CH",
                "colorStyle=0일 때 최댓값 색상",
                GH_ParamAccess.item, Color.FromArgb(215, 25, 28));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M",
                "Z 변위 + 컬러 메시", GH_ParamAccess.item);
            pManager.AddMeshParameter("Flat Mesh", "FM",
                "Z=0 컬러 메시 (지도 오버레이용)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Value", "Min",
                "범례 최솟값", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Value", "Max",
                "범례 최댓값", GH_ParamAccess.item);
            pManager.AddMeshParameter("Legend Mesh", "LM",
                "범례 그라디언트 바", GH_ParamAccess.item);
            pManager.AddGenericParameter("Legend Dots", "LD",
                "범례 값 레이블 (TextDot 리스트)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundary = null;
            var centroids = new List<Point3d>();
            var values = new List<double>();
            double resolution = 100, power = 2.5, heightScale = 0.5;
            double heightRatio = 0.5;
            int legendSteps = 8, colorStyle = 4;
            Color colorLow = Color.FromArgb(44, 123, 182);
            Color colorHigh = Color.FromArgb(215, 25, 28);

            if (!DA.GetData(0, ref boundary)) return;
            if (!DA.GetDataList(1, centroids)) return;
            if (!DA.GetDataList(2, values)) return;
            DA.GetData(3, ref resolution);
            DA.GetData(4, ref power);
            DA.GetData(5, ref heightScale);
            DA.GetData(6, ref heightRatio);
            DA.GetData(7, ref legendSteps);
            DA.GetData(8, ref colorStyle);
            DA.GetData(9, ref colorLow);
            DA.GetData(10, ref colorHigh);

            try
            {
                var viz = new IDWVisualizer(
                    centroids, values,
                    resolution, power, heightScale,
                    heightRatio, legendSteps, colorStyle,
                    colorLow, colorHigh);
                var result = viz.Build(boundary);

                DA.SetData(0, result.Mesh);
                DA.SetData(1, result.FlatMesh);
                DA.SetData(2, result.MinVal);
                DA.SetData(3, result.MaxVal);
                DA.SetData(4, result.LegendMesh);
                DA.SetDataList(5, result.LegendDots);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(
                        ErrorCodes.VisualizationFailed,
                        ErrorMessages.Visualization.VisualizationFailed(ex.Message)));
            }
        }
    }
}
